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

builder.AddRedisClient(ResourceNames.Cache);

builder.AddPlatform(Assembly.GetExecutingAssembly());

builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IUnitOfWork, BookingsUnitOfWork>();

builder.Services.AddHostedService<DatabaseInitializer<BookingsDbContext>>();

builder.AddMessaging<BookingsDbContext>(bus => bus.AddConsumers(Assembly.GetExecutingAssembly()));
builder.AddBusTap(ResourceNames.Services.Bookings);

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
