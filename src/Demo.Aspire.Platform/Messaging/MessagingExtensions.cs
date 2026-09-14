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

        /// <summary>
        /// Tees every integration event this service publishes or consumes onto the bus
        /// tape (see <see cref="BusTap"/>), so the demo UI can show the choreography
        /// happening instead of asserting that it does. Requires a Redis client to have
        /// been registered first, and is meant to be called once, from every service.
        /// </summary>
        /// <param name="serviceName">
        /// The name this service's rows carry on the tape &mdash; the same short name it
        /// is given as an Aspire resource (<see cref="ResourceNames.Services"/>), not the
        /// assembly name, so it lines up with what the rest of the demo already calls it.
        /// </param>
        public IHostApplicationBuilder AddBusTap(string serviceName)
        {
            builder.Services.AddSingleton<BusTap>();

            // Both, because which of the two fires depends on whether the context has an
            // outbox — see the comments on BusTapSendObserver. In this demo it is always
            // the send observer; the publish observer is the safety net.
            builder.Services.AddSendObserver(provider =>
                new BusTapSendObserver(provider.GetRequiredService<BusTap>(), serviceName));

            builder.Services.AddPublishObserver(provider =>
                new BusTapPublishObserver(provider.GetRequiredService<BusTap>(), serviceName));

            builder.Services.AddConsumeObserver(provider =>
                new BusTapConsumeObserver(
                    provider.GetRequiredService<BusTap>(),
                    serviceName,
                    provider.GetRequiredService<TimeProvider>()));

            return builder;
        }

        private IHostApplicationBuilder AddMessagingCore(Action<IBusRegistrationConfigurator>? configure)
        {
            var brokerUri = builder.Configuration.GetConnectionString(ResourceNames.Messaging)
                            ?? throw new InvalidOperationException(
                                $"Connection string '{ResourceNames.Messaging}' was not supplied by the app host.");

            builder.Services.AddMassTransit(registration =>
            {
                registration.SetKebabCaseEndpointNameFormatter();

                configure?.Invoke(registration);

                registration.UsingRabbitMq((context, bus) =>
                {
                    bus.Host(new Uri(brokerUri));

                    bus.UseMessageRetry(retry => retry.Intervals(200, 500, 1_000, 2_000, 5_000));
                    bus.ConfigureEndpoints(context);
                });
            });

            return builder;
        }
    }
}
