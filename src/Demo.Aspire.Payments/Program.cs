using System.Reflection;
using Demo.Aspire.Contracts;
using Demo.Aspire.Payments.Domain;
using Demo.Aspire.Payments.Features.Chaos;
using Demo.Aspire.Payments.Infrastructure;
using Demo.Aspire.Platform;
using Demo.Aspire.Platform.Endpoints;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.Platform.Persistence;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<PaymentsDbContext>(ResourceNames.PaymentsDatabase);

builder.AddRedisClient(ResourceNames.Cache);

builder.AddPlatform(Assembly.GetExecutingAssembly());

builder.Services.Configure<SimulatedPaymentGatewayOptions>(
    builder.Configuration.GetSection(SimulatedPaymentGatewayOptions.SectionName));

builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IUnitOfWork, PaymentsUnitOfWork>();
builder.Services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();
builder.Services.AddSingleton<ChaosSwitch>();

builder.Services.AddHostedService<DatabaseInitializer<PaymentsDbContext>>();

builder.AddMessaging<PaymentsDbContext>(bus => bus.AddConsumers(Assembly.GetExecutingAssembly()));
builder.AddBusTap(ResourceNames.Services.Payments);
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

app.MapOpenApi();
app.MapScalarApiReference(options => options.WithTitle("Payments"));
app.MapDefaultEndpoints();
app.MapEndpoints();

app.Run();
