using Demo.Aspire.Contracts;
using Demo.Aspire.Gateway;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddRedisOutputCache(ResourceNames.Cache);

builder.Services.AddOutputCache(options =>
    options.AddPolicy("catalogue", policy => policy.Expire(TimeSpan.FromSeconds(10))));

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseOutputCache();

app.MapDefaultEndpoints();

app.MapGet("/api/events/recent", (IConnectionMultiplexer redis, CancellationToken cancellationToken, int take = 100) =>
        BusTapeReader.ReadRecentAsync(redis, take, cancellationToken))
    .WithName("GetRecentBusTape");

app.MapGet("/api/events", (IConnectionMultiplexer redis, CancellationToken cancellationToken) =>
        TypedResults.ServerSentEvents(BusTapeReader.StreamAsync(redis, cancellationToken), eventType: "bus"))
    .WithName("StreamBusTape");

app.MapGet("/api/demo", (IConfiguration configuration) =>
        TypedResults.Ok(new DemoInfo(configuration["Demo:DashboardUrl"] ?? string.Empty)))
    .WithName("GetDemoInfo");

app.MapReverseProxy();

app.Run();

internal sealed record DemoInfo(string DashboardUrl);
