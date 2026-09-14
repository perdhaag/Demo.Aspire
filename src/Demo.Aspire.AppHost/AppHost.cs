using Aspire.Hosting.Kubernetes;
using Aspire.Hosting.Kubernetes.Resources;
using Demo.Aspire.Contracts;

var builder = DistributedApplication.CreateBuilder(args);

// The deployment target. Only used in publish mode: `aspire publish` renders the
// whole model below into a Helm chart, `aspire run` ignores it.
// Kubernetes pulls every image rather than building it locally, so each service
// needs a registry to be pushed to -- change the endpoint/repository to your own.
var registry = builder.AddContainerRegistry("registry", "ghcr.io", "perdhaag/demo-aspire");

builder.AddKubernetesEnvironment("k8s")
    .WithContainerRegistry(registry);

// The pushed packages are private to the GitHub account, so the cluster needs credentials
// to pull them. Created out-of-band: see `kubectl create secret docker-registry ghcr-creds`.
static void WithGhcrPullSecret(KubernetesResource resource)
{
    var podSpec = resource.Workload?.PodTemplate.Spec;
    podSpec?.ImagePullSecrets.Add(new LocalObjectReferenceV1 { Name = "ghcr-creds" });
}

var postgres = builder.AddPostgres("postgres")
    .WithPgWeb();

var screeningsDb = postgres.AddDatabase(ResourceNames.ScreeningsDatabase, "screenings");
var bookingsDb = postgres.AddDatabase(ResourceNames.BookingsDatabase, "bookings");
var paymentsDb = postgres.AddDatabase(ResourceNames.PaymentsDatabase, "payments");

var cache = builder.AddRedis(ResourceNames.Cache)
    .WithRedisInsight();

var messaging = builder.AddRabbitMQ(ResourceNames.Messaging)
    .WithManagementPlugin();

var mail = builder.AddContainer(ResourceNames.Mail, "axllent/mailpit")
    .WithHttpEndpoint(targetPort: 8025, name: "web")
    .WithEndpoint(targetPort: 1025, name: "smtp", scheme: "tcp")
    .WithUrlForEndpoint("web", url => url.DisplayText = "Inbox");

var screenings = builder.AddProject<Projects.Demo_Aspire_Screenings>(ResourceNames.Services.Screenings)
    .WithReference(screeningsDb).WaitFor(screeningsDb)
    .WithReference(cache).WaitFor(cache)
    .WithReference(messaging).WaitFor(messaging)
    .WithHttpHealthCheck("/health")
    .PublishAsKubernetesService(WithGhcrPullSecret);

var bookings = builder.AddProject<Projects.Demo_Aspire_Bookings>(ResourceNames.Services.Bookings)
    .WithReference(bookingsDb).WaitFor(bookingsDb)
    .WithReference(cache).WaitFor(cache)
    .WithReference(messaging).WaitFor(messaging)
    .WithReference(screenings)
    .WithHttpHealthCheck("/health")
    .PublishAsKubernetesService(WithGhcrPullSecret);

var payments = builder.AddProject<Projects.Demo_Aspire_Payments>(ResourceNames.Services.Payments)
    .WithReference(paymentsDb).WaitFor(paymentsDb)
    .WithReference(cache).WaitFor(cache)
    .WithReference(messaging).WaitFor(messaging)
    .WithHttpHealthCheck("/health")
    .PublishAsKubernetesService(WithGhcrPullSecret);

var notifications = builder.AddProject<Projects.Demo_Aspire_Notifications>(ResourceNames.Services.Notifications)
    .WithReference(cache).WaitFor(cache)
    .WithReference(messaging).WaitFor(messaging)
    .WithEnvironment("Mail__Host", mail.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("Mail__Port", mail.GetEndpoint("smtp").Property(EndpointProperty.Port))
    .WaitFor(mail)
    .WithHttpHealthCheck("/health")
    .PublishAsKubernetesService(WithGhcrPullSecret);

var dashboardUrl = builder.Configuration["ASPNETCORE_URLS"]?.Split(';')[0];

var gateway = builder.AddProject<Projects.Demo_Aspire_Gateway>(ResourceNames.Services.Gateway)
    .WithReference(cache).WaitFor(cache)
    .WithReference(screenings)
    .WithReference(bookings)
    .WithReference(payments)
    .WithReference(notifications)
    .WithEnvironment("Demo__DashboardUrl", dashboardUrl ?? string.Empty)
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url => url.DisplayText = "Demo Kino")
    .PublishAsKubernetesService(WithGhcrPullSecret);

builder.AddBunApp("web", "../Demo.Aspire.Web", "dev-server.ts")
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithParentRelationship(gateway)
    .WithExplicitStart()
    .WithUrlForEndpoint("http", url => url.DisplayText = "Demo Kino (hot reload)")
    // Dev-time only. In a deployed environment the gateway serves the UI out of its own
    // wwwroot, which its csproj bundles with `bun build` -- so there is nothing for this
    // dev server to do there, and its container would run a dev bundler against
    // NODE_ENV=production anyway.
    .ExcludeFromManifest();
builder.Build().Run();
