using Demo.Aspire.Screenings.Domain;
using Demo.Aspire.SharedKernel;
using Shouldly;

namespace Demo.Aspire.Tests;

/// <summary>
/// The aggregate is pure: no database, no bus, no clock of its own. That is what makes
/// the rules that matter most testable in microseconds.
/// </summary>
public sealed class ScreeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 18, 0, 0, TimeSpan.Zero);

    private static Screening NewScreening(int hoursAhead = 3) =>
        Screening.Schedule(
            "Dune: Part Three",
            "Sal 1",
            Now.AddHours(hoursAhead),
            Money.From(199m),
            rowCount: 3,
            seatsPerRow: 4,
            Now).Value;

    private static SeatNumber Seat(string value) => SeatNumber.Parse(value).Value;

    [Fact]
    public void Scheduling_generates_the_whole_seat_map()
    {
        var screening = NewScreening();

        screening.Seats.Count.ShouldBe(12);
        screening.AvailableSeatCountAt(Now).ShouldBe(12);
        screening.Seats.Select(seat => seat.Number.ToString()).ShouldContain("C4");
    }

    [Fact]
    public void A_screening_cannot_be_scheduled_in_the_past()
    {
        var result = Screening.Schedule(
            "Nosferatu", "Sal 2", Now.AddMinutes(-1), Money.From(120m), 2, 2, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("screening.starts-at");
    }

    [Fact]
    public void Holding_free_seats_succeeds_and_starts_the_clock()
    {
        var screening = NewScreening();
        var booking = new BookingReference(Guid.NewGuid());

        var hold = screening.HoldSeats(booking, [Seat("A1"), Seat("A2")], Now);

        hold.IsSuccess.ShouldBeTrue();
        hold.Value.ExpiresAtUtc.ShouldBe(Now + Screening.DefaultHoldDuration);
        screening.AvailableSeatCountAt(Now).ShouldBe(10);
        screening.DomainEvents.OfType<SeatsHeldForBooking>().Count().ShouldBe(1);
    }

    [Fact]
    public void The_same_seat_cannot_be_held_by_two_bookings()
    {
        var screening = NewScreening();
        screening.HoldSeats(new BookingReference(Guid.NewGuid()), [Seat("A1")], Now);

        var second = screening.HoldSeats(new BookingReference(Guid.NewGuid()), [Seat("A1")], Now);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("seat.taken");
        second.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public void Holding_is_idempotent_so_a_redelivered_message_is_harmless()
    {
        var screening = NewScreening();
        var booking = new BookingReference(Guid.NewGuid());

        var first = screening.HoldSeats(booking, [Seat("B1"), Seat("B2")], Now);
        var again = screening.HoldSeats(booking, [Seat("B1"), Seat("B2")], Now);

        again.IsSuccess.ShouldBeTrue();
        again.Value.Seats.ShouldBe(first.Value.Seats);
        screening.AvailableSeatCountAt(Now).ShouldBe(10);
    }

    [Fact]
    public void A_lapsed_hold_makes_the_seat_available_again()
    {
        var screening = NewScreening();
        var booking = new BookingReference(Guid.NewGuid());
        screening.HoldSeats(booking, [Seat("A1")], Now);

        var later = Now + Screening.DefaultHoldDuration + TimeSpan.FromSeconds(1);

        screening.AvailableSeatCountAt(later).ShouldBe(12);
        screening.ExpireLapsedHolds(later).ShouldBe([booking]);
        screening.DomainEvents.OfType<SeatHoldLapsed>().ShouldHaveSingleItem();
    }

    [Fact]
    public void An_explicit_short_hold_expires_on_its_own_schedule_instead_of_the_default()
    {
        var screening = NewScreening();
        var booking = new BookingReference(Guid.NewGuid());
        var shortHold = TimeSpan.FromSeconds(20);

        var hold = screening.HoldSeats(booking, [Seat("A1")], Now, shortHold);

        hold.Value.ExpiresAtUtc.ShouldBe(Now + shortHold);

        var justBefore = Now + shortHold - TimeSpan.FromSeconds(1);
        screening.AvailableSeatCountAt(justBefore).ShouldBe(11);

        var justAfter = Now + shortHold + TimeSpan.FromSeconds(1);
        screening.AvailableSeatCountAt(justAfter).ShouldBe(12);
        screening.ExpireLapsedHolds(justAfter).ShouldBe([booking]);
    }

    [Fact]
    public void Seats_cannot_be_held_once_the_film_has_started()
    {
        var screening = NewScreening();
        var afterStart = Now.AddHours(4);

        var hold = screening.HoldSeats(new BookingReference(Guid.NewGuid()), [Seat("A1")], afterStart);

        hold.IsFailure.ShouldBeTrue();
        hold.Error.Code.ShouldBe("screening.started");
    }

    [Fact]
    public void A_booking_may_not_take_more_than_the_maximum()
    {
        var screening = NewScreening();
        SeatNumber[] tooMany = [.. screening.Seats.Take(9).Select(seat => seat.Number)];

        var hold = screening.HoldSeats(new BookingReference(Guid.NewGuid()), tooMany, Now);

        hold.IsFailure.ShouldBeTrue();
        hold.Error.Code.ShouldBe("seat.too-many");
    }

    [Fact]
    public void Selling_requires_a_hold_by_the_same_booking()
    {
        var screening = NewScreening();
        var mine = new BookingReference(Guid.NewGuid());
        var theirs = new BookingReference(Guid.NewGuid());
        screening.HoldSeats(mine, [Seat("A1")], Now);

        screening.SellHeldSeats(theirs).IsFailure.ShouldBeTrue();
        screening.SellHeldSeats(mine).IsSuccess.ShouldBeTrue();
        screening.AvailableSeatCountAt(Now.AddHours(1)).ShouldBe(11);
    }

    [Fact]
    public void Releasing_puts_the_seats_back_on_sale()
    {
        var screening = NewScreening();
        var booking = new BookingReference(Guid.NewGuid());
        screening.HoldSeats(booking, [Seat("A1"), Seat("A2")], Now);

        screening.ReleaseSeats(booking, "The payment was declined.").IsSuccess.ShouldBeTrue();

        screening.AvailableSeatCountAt(Now).ShouldBe(12);
        screening.DomainEvents.OfType<SeatsReleased>().ShouldHaveSingleItem();
    }
}
