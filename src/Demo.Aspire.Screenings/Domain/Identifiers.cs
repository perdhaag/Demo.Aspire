namespace Demo.Aspire.Screenings.Domain;

/// <summary>
/// A screening's identity. Strongly typed so that a <see cref="ScreeningId"/> can never
/// be passed where a booking or a seat was meant &mdash; the compiler catches it.
/// </summary>
public readonly record struct ScreeningId(Guid Value)
{
    public static ScreeningId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct SeatId(Guid Value)
{
    public static SeatId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// How this context refers to a booking it does not own. Screenings knows a booking
/// only as an opaque reference; the Bookings context owns everything else about it.
/// </summary>
public readonly record struct BookingReference(Guid Value)
{
    public override string ToString() => Value.ToString();
}
