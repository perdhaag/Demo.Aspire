namespace Demo.Aspire.Contracts;

/// <summary>Screenings accepted the request and is holding the seats until the deadline.</summary>
public sealed record SeatsHeld(
    Guid CorrelationId,
    Guid BookingId,
    Guid ScreeningId,
    string FilmTitle,
    string Auditorium,
    DateTimeOffset ScreeningStartsAtUtc,
    IReadOnlyList<string> Seats,
    DateTimeOffset HoldExpiresAtUtc,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>Screenings refused the request: the seats are gone, or the film has started.</summary>
public sealed record SeatHoldRejected(
    Guid CorrelationId,
    Guid BookingId,
    Guid ScreeningId,
    IReadOnlyList<string> Seats,
    string Reason,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>A hold lapsed before payment completed. Published by the expiry sweeper.</summary>
public sealed record SeatHoldExpired(
    Guid CorrelationId,
    Guid BookingId,
    Guid ScreeningId,
    IReadOnlyList<string> Seats,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
