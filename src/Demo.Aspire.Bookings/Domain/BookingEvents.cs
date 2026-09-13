using Demo.Aspire.SharedKernel;

namespace Demo.Aspire.Bookings.Domain;

public sealed record BookingWasPlaced(
    BookingId BookingId,
    ScreeningReference Screening,
    EmailAddress Customer,
    IReadOnlyList<SeatNumber> Seats,
    Money Total) : DomainEvent;

public sealed record BookingAwaitsPayment(
    BookingId BookingId,
    EmailAddress Customer,
    Money Total,
    DateTimeOffset PayBeforeUtc) : DomainEvent;

public sealed record BookingWasConfirmed(
    BookingId BookingId,
    ScreeningReference Screening,
    ScreeningDetails Details,
    EmailAddress Customer,
    IReadOnlyList<SeatNumber> Seats,
    Money Total,
    string PaymentReference) : DomainEvent;

public sealed record BookingWasCancelled(
    BookingId BookingId,
    ScreeningReference Screening,
    EmailAddress Customer,
    IReadOnlyList<SeatNumber> Seats,
    string Reason) : DomainEvent;
