using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Aspire.Platform.Endpoints;

public static class EndpointExtensions
{
    /// <summary>Registers every <see cref="IEndpoint"/> found in the calling assembly.</summary>
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        ServiceDescriptor[] descriptors =
        [
            .. assembly.DefinedTypes
                .Where(type => type is { IsAbstract: false, IsInterface: false } && type.IsAssignableTo(typeof(IEndpoint)))
                .Select(type => ServiceDescriptor.Transient(typeof(IEndpoint), type)),
        ];

        services.TryAddEnumerableRange(descriptors);
        return services;
    }

    /// <summary>Maps the registered slices, optionally under a shared route group.</summary>
    public static IApplicationBuilder MapEndpoints(this WebApplication app, RouteGroupBuilder? group = null)
    {
        IEndpointRouteBuilder routes = group is null ? app : group;

        foreach (var endpoint in app.Services.GetRequiredService<IEnumerable<IEndpoint>>())
        {
            endpoint.Map(routes);
        }

        return app;
    }

    private static void TryAddEnumerableRange(this IServiceCollection services, IEnumerable<ServiceDescriptor> items)
    {
        foreach (var descriptor in items)
        {
            if (!services.Any(existing =>
                    existing.ServiceType == descriptor.ServiceType
                    && existing.ImplementationType == descriptor.ImplementationType))
            {
                services.Add(descriptor);
            }
        }
    }
}
