namespace Demo.Aspire.Bookings.Domain;

/// <summary>
/// What Bookings knows about a screening: enough to print a ticket, nothing more. The
/// details arrive on the message that confirmed the hold and are kept locally so this
/// context never has to ask Screenings again to answer a question about its own booking.
/// </summary>
public readonly record struct ScreeningDetails(string FilmTitle, string Auditorium, DateTimeOffset StartsAtUtc)
{
    public static ScreeningDetails Unknown => new("(unknown)", "(unknown)", default);
}
