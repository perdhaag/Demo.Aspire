namespace Demo.Aspire.Contracts;

/// <summary>A customer asked for seats. Nothing is reserved yet.</summary>
public sealed record BookingPlaced(
    Guid CorrelationId,
    Guid BookingId,
    Guid ScreeningId,
    string CustomerEmail,
    IReadOnlyList<string> Seats,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>Seats are held and paid for. The booking is final.</summary>
public sealed record BookingConfirmed(
    Guid CorrelationId,
    Guid BookingId,
    Guid ScreeningId,
    string FilmTitle,
    string Auditorium,
    DateTimeOffset ScreeningStartsAtUtc,
    string CustomerEmail,
    IReadOnlyList<string> Seats,
    decimal Amount,
    string Currency,
    string PaymentReference,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>The booking will not happen. Any held seats must be returned to the pool.</summary>
public sealed record BookingCancelled(
    Guid CorrelationId,
    Guid BookingId,
    Guid ScreeningId,
    string CustomerEmail,
    IReadOnlyList<string> Seats,
    string Reason,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
