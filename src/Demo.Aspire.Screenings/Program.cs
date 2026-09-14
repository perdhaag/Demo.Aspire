using System.Reflection;
using Demo.Aspire.Contracts;
using Demo.Aspire.Platform;
using Demo.Aspire.Platform.Endpoints;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.Platform.Persistence;
using Demo.Aspire.Screenings.Domain;
using Demo.Aspire.Screenings.Features.ExpireSeatHolds;
using Demo.Aspire.Screenings.Infrastructure;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Infrastructure, all of it discovered through Aspire's connection strings.
builder.AddNpgsqlDbContext<ScreeningsDbContext>(ResourceNames.ScreeningsDatabase);
builder.AddRedisClientBuilder(ResourceNames.Cache)
    .WithDistributedCache()
    .WithOutputCache();

// One shared Redis connection serves both the seat-map projection and the HTTP
// output cache, so a warm list survives a restart of this service.
builder.AddPlatform(Assembly.GetExecutingAssembly());

builder.Services.AddScoped<IScreeningRepository, ScreeningRepository>();
builder.Services.AddScoped<IUnitOfWork, ScreeningsUnitOfWork>();
builder.Services.AddScoped<IDatabaseSeeder<ScreeningsDbContext>, ScreeningsSeeder>();
builder.Services.AddSingleton<SeatMapProjection>();

// Hosted services start in registration order, so the schema (and with it the outbox
// tables) is in place before MassTransit's delivery service begins polling.
builder.Services.AddHostedService<DatabaseInitializer<ScreeningsDbContext>>();

builder.AddMessaging<ScreeningsDbContext>(bus => bus.AddConsumers(Assembly.GetExecutingAssembly()));
builder.AddBusTap(ResourceNames.Services.Screenings);

builder.Services.AddHostedService<SeatHoldSweeper>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseOutputCache();

app.MapOpenApi();
app.MapScalarApiReference(options => options.WithTitle("Screenings"));
app.MapDefaultEndpoints();
app.MapEndpoints();

app.Run();
