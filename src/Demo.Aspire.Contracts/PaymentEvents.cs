namespace Demo.Aspire.Contracts;

/// <summary>Bookings is ready to be charged. Payments owns how that happens.</summary>
public sealed record PaymentAuthorizationRequested(
    Guid CorrelationId,
    Guid BookingId,
    string CustomerEmail,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>The money moved.</summary>
public sealed record PaymentCaptured(
    Guid CorrelationId,
    Guid BookingId,
    Guid PaymentId,
    string PaymentReference,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>The money did not move, and it never will for this attempt.</summary>
public sealed record PaymentDeclined(
    Guid CorrelationId,
    Guid BookingId,
    Guid PaymentId,
    string Reason,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
