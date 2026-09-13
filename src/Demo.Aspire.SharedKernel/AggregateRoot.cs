namespace Demo.Aspire.SharedKernel;

/// <summary>
/// Non-generic view over an aggregate's pending domain events, so infrastructure can
/// collect them without knowing the identifier type.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    IReadOnlyList<IDomainEvent> DrainDomainEvents();
}

/// <summary>
/// The single entry point into a consistency boundary. Everything inside an aggregate
/// is loaded and saved as one unit, and every invariant it owns holds again by the time
/// each public method returns.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : struct, IEquatable<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id)
    {
    }

    /// <summary>Required by EF Core materialisation only.</summary>
    protected AggregateRoot()
    {
    }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public IReadOnlyList<IDomainEvent> DrainDomainEvents()
    {
        IDomainEvent[] drained = [.. _domainEvents];
        _domainEvents.Clear();
        return drained;
    }
}
