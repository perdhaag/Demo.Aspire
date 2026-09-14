using Demo.Aspire.Contracts;

// Demo Kino — a cinema booking system in five services.
//
//   Gateway ──HTTP──► Screenings ─┐
//      │                          │
//      └──HTTP──► Bookings ◄──────┼── RabbitMQ ──► Payments
//                     │           │
//                     └───────────┴── RabbitMQ ──► Notifications ──SMTP──► Mailpit
//
// Bookings never calls Payments and Payments never calls Screenings. The only thing
// they share is the published language in Demo.Aspire.Contracts.

var builder = DistributedApplication.CreateBuilder(args);

// ── Infrastructure ───────────────────────────────────────────────────────────────

// Deliberately *not* .WithDataVolume(). The services create their schema with
// EnsureCreatedAsync(), which does nothing once the database exists, so a surviving
// volume would silently keep an older schema than the model and every write would fail.
// Ephemeral storage keeps the two in step; add a volume the day this gains migrations.
var postgres = builder.AddPostgres("postgres")
    .WithPgWeb();

// One server, three databases. Each bounded context owns its own schema and no service
// may read another's tables — that is what makes them separable later.
var screeningsDb = postgres.AddDatabase(ResourceNames.ScreeningsDatabase, "screenings");
var bookingsDb = postgres.AddDatabase(ResourceNames.BookingsDatabase, "bookings");
var paymentsDb = postgres.AddDatabase(ResourceNames.PaymentsDatabase, "payments");

var cache = builder.AddRedis(ResourceNames.Cache)
    .WithRedisInsight();

var messaging = builder.AddRabbitMQ(ResourceNames.Messaging)
    .WithManagementPlugin();

// Mailpit is a real SMTP server with a web inbox, so the demo ends in a message you can
// actually open rather than a log line claiming one was sent.
var mail = builder.AddContainer(ResourceNames.Mail, "axllent/mailpit")
    .WithHttpEndpoint(targetPort: 8025, name: "web")
    .WithEndpoint(targetPort: 1025, name: "smtp", scheme: "tcp")
    .WithUrlForEndpoint("web", url => url.DisplayText = "Inbox");

// ── Services ─────────────────────────────────────────────────────────────────────

var screenings = builder.AddProject<Projects.Demo_Aspire_Screenings>(ResourceNames.Services.Screenings)
    .WithReference(screeningsDb).WaitFor(screeningsDb)
    .WithReference(cache).WaitFor(cache)
    .WithReference(messaging).WaitFor(messaging)
    .WithHttpHealthCheck("/health");

var bookings = builder.AddProject<Projects.Demo_Aspire_Bookings>(ResourceNames.Services.Bookings)
    .WithReference(bookingsDb).WaitFor(bookingsDb)
    // Not for its own data — Bookings has none in Redis — but every service tees its
    // published and consumed messages onto the bus tape feed the gateway streams to
    // the demo UI (see Platform/Messaging/BusTap.cs), and that feed lives here.
    .WithReference(cache).WaitFor(cache)
    .WithReference(messaging).WaitFor(messaging)
    // Bookings asks Screenings for a price over HTTP before it creates a booking, so it
    // needs service discovery as well as the bus.
    .WithReference(screenings)
    .WithHttpHealthCheck("/health");

var payments = builder.AddProject<Projects.Demo_Aspire_Payments>(ResourceNames.Services.Payments)
    .WithReference(paymentsDb).WaitFor(paymentsDb)
    // Same reason as Bookings above: the bus tape, not a cache for Payments' own data.
    .WithReference(cache).WaitFor(cache)
    .WithReference(messaging).WaitFor(messaging)
    .WithHttpHealthCheck("/health");

var notifications = builder.AddProject<Projects.Demo_Aspire_Notifications>(ResourceNames.Services.Notifications)
    .WithReference(cache).WaitFor(cache)
    .WithReference(messaging).WaitFor(messaging)
    .WithEnvironment("Mail__Host", mail.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("Mail__Port", mail.GetEndpoint("smtp").Property(EndpointProperty.Port))
    .WaitFor(mail)
    .WithHttpHealthCheck("/health");

// Best-effort only: Aspire does not publish the dashboard's browsable URL to other
// resources the way it does a project's own endpoints (the dashboard is deliberately
// excluded from service discovery), so this reads the app host's own listen address —
// which is what `aspire run` and an IDE launch profile both set for the process that
// hosts the embedded dashboard. When it is not set, the UI simply hides the link.
var dashboardUrl = builder.Configuration["ASPNETCORE_URLS"]?.Split(';')[0];

var gateway = builder.AddProject<Projects.Demo_Aspire_Gateway>(ResourceNames.Services.Gateway)
    .WithReference(cache).WaitFor(cache)
    .WithReference(screenings)
    .WithReference(bookings)
    .WithReference(payments)
    .WithReference(notifications)
    .WithEnvironment("Demo__DashboardUrl", dashboardUrl ?? string.Empty)
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url => url.DisplayText = "Demo Kino");

// ── The front end, for iterating on it ───────────────────────────────────────────
//
// The UI already ships with the application: the gateway's build bundles
// src/Demo.Aspire.Web into its wwwroot with bun, and the "Demo Kino" link above serves
// that bundle. Nothing here is needed to run the demo.
//
// What this adds is the bun dev server, with hot reload, for the times when the thing
// being changed is the page rather than the system behind it — otherwise every CSS tweak
// costs a full dotnet build. It deliberately does not start with everything else:
// starting it would put a second front door on the demo, and the audience should be
// looking at the gateway's. Press Start on it in the dashboard when you want it.
//
// Explicit start also keeps a machine without bun on its PATH from failing the whole app
// host: `aspire run` is unaffected, and the resource only fails if you ask for it. Note
// that mise is activated per shell, so an IDE launched from a desktop menu may not see
// bun even where a terminal does.
//
// It is not the front door, but it is in front of one: the page it serves still asks the
// real gateway for everything under /api, so the Redis output cache, YARP and the four
// services behind them all behave exactly as they do in the built page.
builder.AddBunApp("web", "../Demo.Aspire.Web", "dev-server.ts")
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithParentRelationship(gateway)
    .WithExplicitStart()
    .WithUrlForEndpoint("http", url => url.DisplayText = "Demo Kino (hot reload)");

builder.Build().Run();
