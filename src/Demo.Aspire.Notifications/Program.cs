using System.Reflection;
using Demo.Aspire.Contracts;
using Demo.Aspire.Notifications.Infrastructure;
using Demo.Aspire.Platform;
using Demo.Aspire.Platform.Endpoints;
using Demo.Aspire.Platform.Messaging;
using MassTransit;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddRedisClient(ResourceNames.Cache);

builder.AddPlatform(Assembly.GetExecutingAssembly());

builder.AddMessaging(bus => bus.AddConsumers(Assembly.GetExecutingAssembly()));
builder.AddBusTap(ResourceNames.Services.Notifications);

builder.Services.Configure<MailOptions>(builder.Configuration.GetSection(MailOptions.SectionName));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<NotificationLog>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

app.MapOpenApi();
app.MapScalarApiReference(options => options.WithTitle("Notifications"));
app.MapDefaultEndpoints();
app.MapEndpoints();

app.Run();
