using Demo.Aspire.SharedKernel;

namespace Demo.Aspire.Bookings.Domain;

public enum BookingStatus
{
    /// <summary>Seats have been asked for; Screenings has not answered yet.</summary>
    Placed,

    /// <summary>Seats are held and the clock is ticking on payment.</summary>
    AwaitingPayment,

    Confirmed,

    Cancelled,
}

/// <summary>
/// One customer's attempt to buy seats, from the click to the ticket. The booking is
/// also the process manager for the whole flow: every message that comes back from
/// Screenings or Payments is a request for this aggregate to make the next decision,
/// and the legal transitions live here rather than in a workflow engine.
/// </summary>
public sealed class Booking : AggregateRoot<BookingId>
{
    private Booking(
        BookingId id,
        ScreeningReference screening,
        EmailAddress customer,
        IReadOnlyList<SeatNumber> seats,
        Money total,
        DateTimeOffset placedAtUtc) : base(id)
    {
        Screening = screening;
        Customer = customer;
        Seats = seats;
        Total = total;
        PlacedAtUtc = placedAtUtc;
        Status = BookingStatus.Placed;
        Details = ScreeningDetails.Unknown;
    }

    /// <summary>Required by EF Core materialisation only.</summary>
    private Booking() => Seats = [];

    public ScreeningReference Screening { get; private set; }

    public ScreeningDetails Details { get; private set; }

    public EmailAddress Customer { get; private set; }

    public IReadOnlyList<SeatNumber> Seats { get; private set; }

    public Money Total { get; private set; }

    public BookingStatus Status { get; private set; }

    public DateTimeOffset PlacedAtUtc { get; private set; }

    public DateTimeOffset? PayBeforeUtc { get; private set; }

    /// <summary>When Screenings confirmed the hold.</summary>
    public DateTimeOffset? SeatsHeldAtUtc { get; private set; }

    /// <summary>When the booking reached its final state, either way.</summary>
    public DateTimeOffset? FinishedAtUtc { get; private set; }

    public string? PaymentReference { get; private set; }

    public string? CancellationReason { get; private set; }

    public bool IsFinished => Status is BookingStatus.Confirmed or BookingStatus.Cancelled;

    public static Result<Booking> Place(
        ScreeningReference screening,
        EmailAddress customer,
        IReadOnlyList<SeatNumber> seats,
        Money ticketPrice,
        DateTimeOffset now)
    {
        if (seats.Count == 0)
        {
            return Error.Validation("booking.no-seats", "A booking must be for at least one seat.");
        }

        if (seats.Distinct().Count() != seats.Count)
        {
            return Error.Validation("booking.duplicate-seats", "The same seat was requested more than once.");
        }

        var booking = new Booking(
            BookingId.New(),
            screening,
            customer,
            [.. seats.Order()],
            ticketPrice.Times(seats.Count),
            now);

        booking.Raise(new BookingWasPlaced(
            booking.Id,
            booking.Screening,
            booking.Customer,
            booking.Seats,
            booking.Total));

        return booking;
    }

    /// <summary>Screenings is holding the seats. The customer now owes us money.</summary>
    public Result SeatsWereHeld(ScreeningDetails details, DateTimeOffset payBeforeUtc, DateTimeOffset now)
    {
        if (Status is BookingStatus.AwaitingPayment)
        {
            return Result.Success(); // Redelivered message; the decision already stands.
        }

        if (Status is not BookingStatus.Placed)
        {
            return Error.Conflict(
                "booking.not-pending",
                $"A booking that is {Status} cannot start awaiting payment.");
        }

        Details = details;
        PayBeforeUtc = payBeforeUtc;
        SeatsHeldAtUtc = now;
        Status = BookingStatus.AwaitingPayment;

        Raise(new BookingAwaitsPayment(Id, Customer, Total, payBeforeUtc));
        return Result.Success();
    }

    /// <summary>The money arrived in time. This is the only path to a ticket.</summary>
    public Result Confirm(string paymentReference, DateTimeOffset now)
    {
        if (Status is BookingStatus.Confirmed)
        {
            return Result.Success();
        }

        if (Status is not BookingStatus.AwaitingPayment)
        {
            return Error.Conflict(
                "booking.not-awaiting-payment",
                $"A booking that is {Status} cannot be confirmed.");
        }

        PaymentReference = Guard.AgainstNullOrWhiteSpace(paymentReference);
        FinishedAtUtc = now;
        Status = BookingStatus.Confirmed;

        Raise(new BookingWasConfirmed(Id, Screening, Details, Customer, Seats, Total, PaymentReference));
        return Result.Success();
    }

    /// <summary>
    /// Ends the booking. Anything that can go wrong &mdash; refused seats, a declined
    /// card, a lapsed hold &mdash; arrives here, and a confirmed booking refuses to be
    /// cancelled by a late message.
    /// </summary>
    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (Status is BookingStatus.Cancelled)
        {
            return Result.Success();
        }

        if (Status is BookingStatus.Confirmed)
        {
            return Error.Conflict("booking.already-confirmed", "A confirmed booking cannot be cancelled here.");
        }

        CancellationReason = Guard.AgainstNullOrWhiteSpace(reason);
        FinishedAtUtc = now;
        Status = BookingStatus.Cancelled;

        Raise(new BookingWasCancelled(Id, Screening, Customer, Seats, CancellationReason));
        return Result.Success();
    }
}
