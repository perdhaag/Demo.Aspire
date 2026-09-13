using Demo.Aspire.SharedKernel;

namespace Demo.Aspire.Screenings.Domain;

public enum SeatStatus
{
    Available,
    Held,
    Sold,
}

/// <summary>
/// An entity inside the <see cref="Screening"/> aggregate. It has identity and a life
/// cycle of its own, but it may only be changed through the screening that owns it,
/// which is why every mutator is internal.
/// </summary>
public sealed class Seat : Entity<SeatId>
{
    private Seat(SeatId id, SeatNumber number) : base(id) => Number = number;

    /// <summary>Required by EF Core materialisation only.</summary>
    private Seat()
    {
    }

    public SeatNumber Number { get; private set; }

    public SeatStatus Status { get; private set; }

    public BookingReference? HeldBy { get; private set; }

    public DateTimeOffset? HoldExpiresAtUtc { get; private set; }

    internal static Seat Create(SeatNumber number) => new(SeatId.New(), number);

    /// <summary>
    /// A seat whose hold has run out is available again, even before the sweeper has
    /// noticed. Time is part of the invariant, so it is passed in rather than read from
    /// a static clock.
    /// </summary>
    internal bool IsAvailableAt(DateTimeOffset now) => Status switch
    {
        SeatStatus.Available => true,
        SeatStatus.Held => HoldExpiresAtUtc <= now,
        _ => false,
    };

    internal bool IsHeldBy(BookingReference booking) => Status is SeatStatus.Held && HeldBy == booking;

    internal bool IsSoldTo(BookingReference booking) => Status is SeatStatus.Sold && HeldBy == booking;

    internal void HoldFor(BookingReference booking, DateTimeOffset expiresAtUtc)
    {
        Status = SeatStatus.Held;
        HeldBy = booking;
        HoldExpiresAtUtc = expiresAtUtc;
    }

    internal void Sell()
    {
        if (Status is not SeatStatus.Held)
        {
            throw new InvalidOperationException($"Seat {Number} cannot be sold from state {Status}.");
        }

        Status = SeatStatus.Sold;
        HoldExpiresAtUtc = null;
    }

    internal void Release()
    {
        Status = SeatStatus.Available;
        HeldBy = null;
        HoldExpiresAtUtc = null;
    }
}
