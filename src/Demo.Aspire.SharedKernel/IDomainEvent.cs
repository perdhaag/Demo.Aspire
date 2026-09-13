namespace Demo.Aspire.SharedKernel;

/// <summary>
/// Something that happened inside a single aggregate. Domain events never leave the
/// process on their own &mdash; a handler decides whether to translate one into an
/// integration event on the bus.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAtUtc { get; }
}

/// <summary>Convenience base record so concrete events stay one line long.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAtUtc { get; init; } = TimeProvider.System.GetUtcNow();
}
