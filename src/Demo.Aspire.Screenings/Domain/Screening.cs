using Demo.Aspire.SharedKernel;

namespace Demo.Aspire.Screenings.Domain;

/// <summary>
/// A showing of one film in one auditorium at one time, together with every seat in
/// that room. The screening is the consistency boundary: two customers may never hold
/// the same seat, and that rule is enforced here rather than in a handler, a database
/// constraint or the UI.
/// </summary>
public sealed class Screening : AggregateRoot<ScreeningId>
{
    /// <summary>How long a customer gets to pay before the seats go back on sale.</summary>
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(3);

    public const int MaxSeatsPerBooking = 8;

    private readonly List<Seat> _seats = [];

    private Screening(
        ScreeningId id,
        string filmTitle,
        string auditorium,
        DateTimeOffset startsAtUtc,
        Money ticketPrice,
        IEnumerable<Seat> seats) : base(id)
    {
        FilmTitle = filmTitle;
        Auditorium = auditorium;
        StartsAtUtc = startsAtUtc;
        TicketPrice = ticketPrice;
        _seats.AddRange(seats);
    }

    /// <summary>Required by EF Core materialisation only.</summary>
    private Screening()
    {
        FilmTitle = null!;
        Auditorium = null!;
    }

    public string FilmTitle { get; private set; }

    public string Auditorium { get; private set; }

    public DateTimeOffset StartsAtUtc { get; private set; }

    public Money TicketPrice { get; private set; }

    public IReadOnlyList<Seat> Seats => _seats;

    public int AvailableSeatCountAt(DateTimeOffset now) => _seats.Count(seat => seat.IsAvailableAt(now));

    public bool HasStartedAt(DateTimeOffset now) => now >= StartsAtUtc;

    /// <summary>
    /// Creates a screening with a freshly generated seat map. The factory is the only
    /// way in, so a screening can never exist with zero seats or a start time in the past.
    /// </summary>
    public static Result<Screening> Schedule(
        string filmTitle,
        string auditorium,
        DateTimeOffset startsAtUtc,
        Money ticketPrice,
        int rowCount,
        int seatsPerRow,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(filmTitle))
        {
            return Error.Validation("screening.film-title", "A screening needs a film title.");
        }

        if (string.IsNullOrWhiteSpace(auditorium))
        {
            return Error.Validation("screening.auditorium", "A screening needs an auditorium.");
        }

        if (startsAtUtc <= now)
        {
            return Error.Validation("screening.starts-at", "A screening must start in the future.");
        }

        if (rowCount is < 1 or > 26)
        {
            return Error.Validation("screening.rows", "An auditorium has between 1 and 26 rows.");
        }

        if (seatsPerRow is < 1 or > 99)
        {
            return Error.Validation("screening.seats-per-row", "A row holds between 1 and 99 seats.");
        }

        List<Seat> seats = [];

        for (var row = 0; row < rowCount; row++)
        {
            for (var number = 1; number <= seatsPerRow; number++)
            {
                var seatNumber = SeatNumber.Create((char)('A' + row), number);

                if (seatNumber.IsFailure)
                {
                    return seatNumber.Error;
                }

                seats.Add(Seat.Create(seatNumber.Value));
            }
        }

        var screening = new Screening(
            ScreeningId.New(),
            filmTitle.Trim(),
            auditorium.Trim(),
            startsAtUtc,
            ticketPrice,
            seats);

        screening.Raise(new ScreeningScheduled(
            screening.Id,
            screening.FilmTitle,
            screening.Auditorium,
            screening.StartsAtUtc,
            seats.Count));

        return screening;
    }

    /// <summary>
    /// Reserves seats for a booking, or explains why it cannot. Calling it twice for the
    /// same booking is safe: the second call reports the hold that already exists, which
    /// is what makes the message handler idempotent under redelivery.
    /// </summary>
    public Result<SeatHold> HoldSeats(
        BookingReference booking,
        IReadOnlyCollection<SeatNumber> requested,
        DateTimeOffset now)
    {
        if (_seats.Any(seat => seat.IsHeldBy(booking) || seat.IsSoldTo(booking)))
        {
            SeatNumber[] existing =
            [
                .. _seats.Where(seat => seat.IsHeldBy(booking) || seat.IsSoldTo(booking))
                    .Select(seat => seat.Number)
                    .Order(),
            ];

            return new SeatHold(existing, _seats.First(seat => seat.IsHeldBy(booking) || seat.IsSoldTo(booking))
                .HoldExpiresAtUtc ?? StartsAtUtc);
        }

        var validation = ValidateRequest(requested, now);

        if (validation.IsFailure)
        {
            Raise(new SeatHoldRefused(Id, booking, [.. requested], validation.Error));
            return validation.Error;
        }

        List<Seat> matched = [];

        foreach (var number in requested)
        {
            var seat = _seats.SingleOrDefault(candidate => candidate.Number == number);

            if (seat is null)
            {
                var unknown = Error.NotFound("seat.unknown", $"Seat {number} does not exist in {Auditorium}.");
                Raise(new SeatHoldRefused(Id, booking, [.. requested], unknown));
                return unknown;
            }

            if (!seat.IsAvailableAt(now))
            {
                var taken = Error.Conflict("seat.taken", $"Seat {number} is no longer available.");
                Raise(new SeatHoldRefused(Id, booking, [.. requested], taken));
                return taken;
            }

            matched.Add(seat);
        }

        var expiresAtUtc = now + HoldDuration;

        foreach (var seat in matched)
        {
            seat.HoldFor(booking, expiresAtUtc);
        }

        SeatNumber[] held = [.. matched.Select(seat => seat.Number).Order()];

        Raise(new SeatsHeldForBooking(Id, booking, FilmTitle, Auditorium, StartsAtUtc, held, expiresAtUtc));

        return new SeatHold(held, expiresAtUtc);
    }

    /// <summary>Turns a hold into a sale. Only the booking that holds the seats may do this.</summary>
    public Result SellHeldSeats(BookingReference booking)
    {
        Seat[] held = [.. _seats.Where(seat => seat.IsHeldBy(booking))];

        if (held.Length == 0)
        {
            return _seats.Any(seat => seat.IsSoldTo(booking))
                ? Result.Success() // Already sold: a redelivered confirmation, nothing to do.
                : Error.Conflict("seat.no-hold", $"Booking {booking} holds no seats on this screening.");
        }

        foreach (var seat in held)
        {
            seat.Sell();
        }

        Raise(new SeatsSold(Id, booking, [.. held.Select(seat => seat.Number).Order()]));
        return Result.Success();
    }

    /// <summary>Returns a booking's seats to the pool. Safe to call when nothing is held.</summary>
    public Result ReleaseSeats(BookingReference booking, string reason)
    {
        Seat[] held = [.. _seats.Where(seat => seat.IsHeldBy(booking) || seat.IsSoldTo(booking))];

        if (held.Length == 0)
        {
            return Result.Success();
        }

        SeatNumber[] numbers = [.. held.Select(seat => seat.Number).Order()];

        foreach (var seat in held)
        {
            seat.Release();
        }

        Raise(new SeatsReleased(Id, booking, numbers, reason));
        return Result.Success();
    }

    /// <summary>
    /// Frees every hold that has run out. Returns the bookings that lost their seats so
    /// the caller can tell the rest of the system.
    /// </summary>
    public IReadOnlyList<BookingReference> ExpireLapsedHolds(DateTimeOffset now)
    {
        BookingReference[] lapsed =
        [
            .. _seats
                .Where(seat => seat is { Status: SeatStatus.Held, HeldBy: not null } && seat.HoldExpiresAtUtc <= now)
                .Select(seat => seat.HeldBy!.Value)
                .Distinct(),
        ];

        foreach (var booking in lapsed)
        {
            SeatNumber[] numbers = [.. _seats.Where(seat => seat.IsHeldBy(booking)).Select(seat => seat.Number).Order()];

            foreach (var seat in _seats.Where(seat => seat.IsHeldBy(booking)).ToArray())
            {
                seat.Release();
            }

            Raise(new SeatHoldLapsed(Id, booking, numbers));
        }

        return lapsed;
    }

    private Result ValidateRequest(IReadOnlyCollection<SeatNumber> requested, DateTimeOffset now)
    {
        if (requested.Count == 0)
        {
            return Error.Validation("seat.none-requested", "At least one seat must be requested.");
        }

        if (requested.Count > MaxSeatsPerBooking)
        {
            return Error.Validation(
                "seat.too-many",
                $"A single booking may hold at most {MaxSeatsPerBooking} seats.");
        }

        if (requested.Distinct().Count() != requested.Count)
        {
            return Error.Validation("seat.duplicate", "The same seat was requested more than once.");
        }

        return HasStartedAt(now)
            ? Error.Conflict("screening.started", $"'{FilmTitle}' has already started.")
            : Result.Success();
    }
}

/// <summary>The outcome of a successful hold: which seats, and until when.</summary>
public readonly record struct SeatHold(IReadOnlyList<SeatNumber> Seats, DateTimeOffset ExpiresAtUtc);
