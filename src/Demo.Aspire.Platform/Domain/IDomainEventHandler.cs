using Demo.Aspire.SharedKernel;

namespace Demo.Aspire.Platform.Domain;

/// <summary>
/// Reacts to something an aggregate decided. Handlers run inside the same transaction
/// that saved the aggregate, which is what makes the outbox reliable.
/// </summary>
public interface IDomainEventHandler<in TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    Task HandleAsync(TDomainEvent domainEvent, CancellationToken cancellationToken);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}
