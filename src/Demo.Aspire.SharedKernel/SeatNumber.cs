using System.Globalization;

namespace Demo.Aspire.SharedKernel;

/// <summary>
/// A seat in an auditorium, such as <c>C12</c>. Part of the shared kernel because both
/// Screenings and Bookings reason about the very same concept and must agree on it.
/// </summary>
public readonly record struct SeatNumber : IComparable<SeatNumber>
{
    private SeatNumber(char row, int number)
    {
        Row = row;
        Number = number;
    }

    public char Row { get; }

    public int Number { get; }

    public static Result<SeatNumber> Create(char row, int number)
    {
        var upper = char.ToUpperInvariant(row);

        if (upper is < 'A' or > 'Z')
        {
            return Error.Validation("seat.row", "A seat row must be a letter between A and Z.");
        }

        return number is < 1 or > 99
            ? Error.Validation("seat.number", "A seat number must be between 1 and 99.")
            : new SeatNumber(upper, number);
    }

    public static Result<SeatNumber> Parse(string? value)
    {
        var candidate = value?.Trim();

        return candidate is { Length: >= 2 } text
               && char.IsAsciiLetter(text[0])
               && int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? Create(text[0], number)
            : Error.Validation("seat.format", $"'{value}' is not a seat number such as 'C12'.");
    }

    public int CompareTo(SeatNumber other)
    {
        var byRow = Row.CompareTo(other.Row);
        return byRow != 0 ? byRow : Number.CompareTo(other.Number);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Row}{Number}");
}
