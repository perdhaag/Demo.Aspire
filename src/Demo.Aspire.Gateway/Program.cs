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

app.MapReverseProxy();

app.Run();
