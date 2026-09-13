using Demo.Aspire.SharedKernel;

namespace Demo.Aspire.Screenings.Domain;

public sealed record ScreeningScheduled(
    ScreeningId ScreeningId,
    string FilmTitle,
    string Auditorium,
    DateTimeOffset StartsAtUtc,
    int SeatCount) : DomainEvent;

public sealed record SeatsHeldForBooking(
    ScreeningId ScreeningId,
    BookingReference Booking,
    string FilmTitle,
    string Auditorium,
    DateTimeOffset ScreeningStartsAtUtc,
    IReadOnlyList<SeatNumber> Seats,
    DateTimeOffset HoldExpiresAtUtc) : DomainEvent;

public sealed record SeatHoldRefused(
    ScreeningId ScreeningId,
    BookingReference Booking,
    IReadOnlyList<SeatNumber> Seats,
    Error Reason) : DomainEvent;

public sealed record SeatsSold(
    ScreeningId ScreeningId,
    BookingReference Booking,
    IReadOnlyList<SeatNumber> Seats) : DomainEvent;

public sealed record SeatsReleased(
    ScreeningId ScreeningId,
    BookingReference Booking,
    IReadOnlyList<SeatNumber> Seats,
    string Reason) : DomainEvent;

public sealed record SeatHoldLapsed(
    ScreeningId ScreeningId,
    BookingReference Booking,
    IReadOnlyList<SeatNumber> Seats) : DomainEvent;
