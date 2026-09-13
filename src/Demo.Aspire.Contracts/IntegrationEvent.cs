namespace Demo.Aspire.Contracts;

/// <summary>
/// Marker for messages that cross a bounded-context boundary. Integration events are
/// facts about the past, expressed in primitives so that every consumer can translate
/// them into its own model.
/// </summary>
public interface IIntegrationEvent
{
    Guid CorrelationId { get; }

    DateTimeOffset OccurredAtUtc { get; }
}
