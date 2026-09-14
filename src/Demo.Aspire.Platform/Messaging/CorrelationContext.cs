namespace Demo.Aspire.Platform.Messaging;

/// <summary>
/// Carries the identity of the conversation through one scope, so a domain-event
/// handler can stamp the integration event it publishes with the same correlation id
/// that arrived on the message it is reacting to. Without it the domain model would
/// have to know about messaging just to keep a trace intact.
/// </summary>
public sealed class CorrelationContext
{
    public Guid Current
    {
        get => field == Guid.Empty ? field = Guid.CreateVersion7() : field;
        set;
    }
}
