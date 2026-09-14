using System.Reflection;
using Demo.Aspire.Bookings.Domain;
using Demo.Aspire.Bookings.Infrastructure;
using Demo.Aspire.Contracts;
using Demo.Aspire.Platform;
using Demo.Aspire.Platform.Endpoints;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.Platform.Persistence;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<BookingsDbContext>(ResourceNames.BookingsDatabase);

// This context has no data of its own in Redis; it is here only so the bus tape (see
// AddBusTap below) has somewhere to write.
builder.AddRedisClient(ResourceNames.Cache);

builder.AddPlatform(Assembly.GetExecutingAssembly());

builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IUnitOfWork, BookingsUnitOfWork>();

// Registered before the bus so the outbox tables exist by the time it starts.
builder.Services.AddHostedService<DatabaseInitializer<BookingsDbContext>>();

builder.AddMessaging<BookingsDbContext>(bus => bus.AddConsumers(Assembly.GetExecutingAssembly()));
builder.AddBusTap(ResourceNames.Services.Bookings);

// "https+http://screenings" is resolved by Aspire's service discovery, and the standard
// resilience handler from the service defaults adds the retries and timeouts.
builder.Services.AddHttpClient<IScreeningCatalog, ScreeningCatalogClient>(client =>
    client.BaseAddress = new Uri($"https+http://{ResourceNames.Services.Screenings}"));

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

app.MapOpenApi();
app.MapScalarApiReference(options => options.WithTitle("Bookings"));
app.MapDefaultEndpoints();
app.MapEndpoints();

app.Run();
