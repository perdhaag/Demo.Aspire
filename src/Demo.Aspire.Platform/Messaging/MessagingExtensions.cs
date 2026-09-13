using Demo.Aspire.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Demo.Aspire.Platform.Messaging;

public static class MessagingExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Wires a bounded context onto the bus with a transactional outbox and inbox.
        /// Publishing writes a row in the same transaction as the aggregate change, and a
        /// delivery service moves it to RabbitMQ afterwards, so a crash between "state
        /// changed" and "everyone was told" cannot lose the message.
        /// </summary>
        public IHostApplicationBuilder AddMessaging<TDbContext>(Action<IBusRegistrationConfigurator>? configure = null)
            where TDbContext : DbContext =>
            builder.AddMessagingCore(registration =>
            {
                registration.AddEntityFrameworkOutbox<TDbContext>(outbox =>
                {
                    outbox.UsePostgres();
                    outbox.UseBusOutbox();
                    outbox.QueryDelay = TimeSpan.FromSeconds(1);
                });

                registration.AddConfigureEndpointsCallback((context, _, endpoint) =>
                {
                    // The inbox makes consumers idempotent: a redelivered message is
                    // recognised and acknowledged without running the handler twice.
                    endpoint.UseEntityFrameworkOutbox<TDbContext>(context);
                });

                configure?.Invoke(registration);
            });

        /// <summary>
        /// Wires a stateless bounded context onto the bus. Without a database there is no
        /// outbox, so such a service may consume but should not publish decisions it
        /// would need to recover.
        /// </summary>
        public IHostApplicationBuilder AddMessaging(Action<IBusRegistrationConfigurator>? configure = null) =>
            builder.AddMessagingCore(configure);

        private IHostApplicationBuilder AddMessagingCore(Action<IBusRegistrationConfigurator>? configure)
        {
            var brokerUri = builder.Configuration.GetConnectionString(ResourceNames.Messaging)
                            ?? throw new InvalidOperationException(
                                $"Connection string '{ResourceNames.Messaging}' was not supplied by the app host.");

            builder.Services.AddMassTransit(registration =>
            {
                registration.SetKebabCaseEndpointNameFormatter();

                // Note for slice authors: MassTransit's assembly scan only picks up
                // *public* consumer types. An internal consumer is registered silently as
                // nothing at all — the bus starts, no receive endpoint is created, and the
                // messages it should have handled are dropped by the broker.
                configure?.Invoke(registration);

                registration.UsingRabbitMq((context, bus) =>
                {
                    bus.Host(new Uri(brokerUri));

                    // Transient faults (a locked row, a blipping connection) are worth a
                    // few immediate retries; anything that survives them goes to _error.
                    bus.UseMessageRetry(retry => retry.Intervals(200, 500, 1_000, 2_000, 5_000));
                    bus.ConfigureEndpoints(context);
                });
            });

            return builder;
        }
    }
}
