using System.Text.Json.Serialization;

namespace Demo.Aspire.Contracts;

/// <summary>What a bus tape row is reporting.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BusTapKind>))]
public enum BusTapKind
{
    Published,
    Consumed,
    Faulted,
}

/// <summary>
/// One line of the bus tape: one integration event published, consumed, or faulted on
/// while being consumed. Every service writes these to a shared Redis feed and the
/// gateway streams them to the browser, so the demo UI can show the choreography
/// happening rather than assert that it does. It is data about the published language,
/// not part of it &mdash; it never crosses the bus itself &mdash; but it belongs here
/// rather than duplicated per service because every service that writes one and the one
/// gateway that reads them all need the same shape.
/// </summary>
public sealed record BusTapEntry(
    Guid MessageId,
    Guid CorrelationId,
    string Event,
    string Service,
    BusTapKind Kind,
    DateTimeOffset AtUtc,
    double? DurationMs,
    string? TraceId,
    string? Detail);

/// <summary>
/// The Redis names every writer and the one reader have to agree on &mdash; the same
/// reason <see cref="ResourceNames"/> lives here rather than as a literal repeated in
/// each service.
/// </summary>
public static class BusTapKeys
{
    /// <summary>The capped list a client reads for history on first connecting.</summary>
    public const string FeedKey = "bus:tape";

    /// <summary>The pub/sub channel a new entry is published on as it happens.</summary>
    public const string ChannelName = "bus:events";
}
