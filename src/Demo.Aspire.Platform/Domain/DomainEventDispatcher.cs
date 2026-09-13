using System.Collections.Concurrent;
using System.Reflection;
using Demo.Aspire.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Platform.Domain;

internal sealed class DomainEventDispatcher(IServiceProvider services, ILogger<DomainEventDispatcher> logger)
    : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Invoker> Invokers = new();

    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            var invoker = Invokers.GetOrAdd(domainEvent.GetType(), Invoker.For);

            foreach (var handler in invoker.ResolveHandlers(services))
            {
                logger.LogDebug(
                    "Dispatching {DomainEvent} to {Handler}",
                    domainEvent.GetType().Name,
                    handler.GetType().Name);

                await invoker.InvokeAsync(handler, domainEvent, cancellationToken);
            }
        }
    }

    private sealed class Invoker(Type handlerType, MethodInfo method)
    {
        public static Invoker For(Type eventType)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
            return new Invoker(handlerType, method);
        }

        public IEnumerable<object> ResolveHandlers(IServiceProvider provider) =>
            (IEnumerable<object>)provider.GetServices(handlerType).Where(handler => handler is not null)!;

        public Task InvokeAsync(object handler, IDomainEvent domainEvent, CancellationToken cancellationToken) =>
            (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
    }
}
