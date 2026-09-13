namespace Demo.Aspire.Bookings.Domain;

public readonly record struct BookingId(Guid Value)
{
    public static BookingId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// How Bookings refers to a screening. The Screenings context owns the seat map; all
/// this context keeps is the identifier plus whatever it was told.
/// </summary>
public readonly record struct ScreeningReference(Guid Value)
{
    public override string ToString() => Value.ToString();
}
