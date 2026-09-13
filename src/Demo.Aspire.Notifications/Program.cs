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

// No database here, so no outbox: this context only reacts, it never decides anything
// it would need to recover after a crash.
builder.AddMessaging(bus => bus.AddConsumers(Assembly.GetExecutingAssembly()));

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
