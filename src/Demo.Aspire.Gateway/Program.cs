using Demo.Aspire.Contracts;
using Demo.Aspire.Gateway;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The gateway's Redis is the shared one: a seat list cached here is served to every
// browser without any of the four services behind it being touched at all, and it is
// also where every service's bus tape entries land (see BusTapeReader).
builder.AddRedisOutputCache(ResourceNames.Cache);

builder.Services.AddOutputCache(options =>
    options.AddPolicy("catalogue", policy => policy.Expire(TimeSpan.FromSeconds(10))));

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    // Destinations are written as "https+http://bookings"; Aspire resolves them to the
    // real endpoints it assigned, so no port numbers appear in configuration.
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseOutputCache();

app.MapDefaultEndpoints();

// The gateway's own routes, mapped before the proxy so neither one needs to know about
// the other. No YARP route matches "/api/events*", so this never competes with one.
app.MapGet("/api/events/recent", (IConnectionMultiplexer redis, CancellationToken cancellationToken, int take = 100) =>
        BusTapeReader.ReadRecentAsync(redis, take, cancellationToken))
    .WithName("GetRecentBusTape");

app.MapGet("/api/events", (IConnectionMultiplexer redis, CancellationToken cancellationToken) =>
        TypedResults.ServerSentEvents(BusTapeReader.StreamAsync(redis, cancellationToken), eventType: "bus"))
    .WithName("StreamBusTape");

// Best-effort, per the app host's own comment on Demo__DashboardUrl: an empty string
// here just means the UI hides the link rather than guessing at one.
app.MapGet("/api/demo", (IConfiguration configuration) =>
        TypedResults.Ok(new DemoInfo(configuration["Demo:DashboardUrl"] ?? string.Empty)))
    .WithName("GetDemoInfo");

app.MapReverseProxy();

app.Run();

/// <summary>What the UI needs to know about the demo environment itself, not about any
/// one booking. Currently just the dashboard link for the trace waterfall.</summary>
internal sealed record DemoInfo(string DashboardUrl);
