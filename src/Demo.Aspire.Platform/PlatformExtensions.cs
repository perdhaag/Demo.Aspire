using System.Reflection;
using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Endpoints;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Demo.Aspire.Platform;

public static class PlatformExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Registers the pieces every slice assumes: the domain-event dispatcher, the
        /// slices' endpoints, their validators and their domain-event handlers.
        /// </summary>
        public IHostApplicationBuilder AddPlatform(Assembly slicesAssembly)
        {
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
            builder.Services.AddScoped<Messaging.CorrelationContext>();
            builder.Services.AddEndpoints(slicesAssembly);
            builder.Services.AddValidatorsFromAssembly(slicesAssembly, includeInternalTypes: true);
            builder.Services.AddDomainEventHandlers(slicesAssembly);
            builder.Services.AddSliceHandlers(slicesAssembly);
            builder.Services.AddProblemDetails();

            return builder;
        }
    }

    /// <summary>
    /// Registers the behaviour of every slice by convention. A slice's handler is only
    /// ever resolved by its own endpoint or consumer, so it needs no interface and no
    /// entry in a shared registration file.
    /// </summary>
    private static IServiceCollection AddSliceHandlers(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.DefinedTypes.Where(type =>
                     type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                     && type.Name.EndsWith("Handler", StringComparison.Ordinal)))
        {
            services.AddScoped(type);
        }

        return services;
    }

    private static IServiceCollection AddDomainEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.DefinedTypes.Where(type => type is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var handlerInterface in type.ImplementedInterfaces.Where(IsDomainEventHandler))
            {
                services.AddScoped(handlerInterface, type);
            }
        }

        return services;

        static bool IsDomainEventHandler(Type candidate) =>
            candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>);
    }
}
