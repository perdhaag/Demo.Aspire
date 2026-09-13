using Demo.Aspire.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The gateway's Redis is the shared one: a seat list cached here is served to every
// browser without any of the four services behind it being touched at all.
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
app.MapReverseProxy();

app.Run();
