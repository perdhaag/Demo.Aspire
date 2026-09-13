using Demo.Aspire.Bookings.Domain;
using Demo.Aspire.SharedKernel;
using Shouldly;

namespace Demo.Aspire.Tests;

/// <summary>
/// The booking is the process manager, so these tests are really tests of the flow:
/// which messages are allowed to move it on, and which arrive too late to matter.
/// </summary>
public sealed class BookingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 18, 0, 0, TimeSpan.Zero);

    private static readonly ScreeningDetails Details = new("Dune: Part Three", "Sal 1", Now.AddHours(3));

    private static Booking NewBooking() => Booking.Place(
        new ScreeningReference(Guid.NewGuid()),
        EmailAddress.Create("ada@example.com").Value,
        [SeatNumber.Parse("A1").Value, SeatNumber.Parse("A2").Value],
        Money.From(199m),
        Now).Value;

    [Fact]
    public void Placing_prices_the_whole_order()
    {
        var booking = NewBooking();

        booking.Total.ShouldBe(Money.From(398m));
        booking.Status.ShouldBe(BookingStatus.Placed);
        booking.DomainEvents.OfType<BookingWasPlaced>().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_booking_needs_at_least_one_seat()
    {
        var result = Booking.Place(
            new ScreeningReference(Guid.NewGuid()),
            EmailAddress.Create("ada@example.com").Value,
            [],
            Money.From(199m),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("booking.no-seats");
    }

    [Fact]
    public void The_happy_path_ends_confirmed()
    {
        var booking = NewBooking();

        booking.SeatsWereHeld(Details, Now.AddMinutes(3), Now).IsSuccess.ShouldBeTrue();
        booking.Status.ShouldBe(BookingStatus.AwaitingPayment);

        booking.Confirm("AUTH-20260913-ABCDE", Now).IsSuccess.ShouldBeTrue();
        booking.Status.ShouldBe(BookingStatus.Confirmed);
        booking.Details.FilmTitle.ShouldBe("Dune: Part Three");
        booking.SeatsHeldAtUtc.ShouldBe(Now);
        booking.FinishedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void Payment_cannot_be_taken_before_the_seats_are_held()
    {
        var booking = NewBooking();

        var result = booking.Confirm("AUTH-1", Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("booking.not-awaiting-payment");
    }

    [Fact]
    public void A_confirmed_booking_refuses_a_late_cancellation()
    {
        var booking = NewBooking();
        booking.SeatsWereHeld(Details, Now.AddMinutes(3), Now);
        booking.Confirm("AUTH-1", Now);

        var result = booking.Cancel("The hold expired.", Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("booking.already-confirmed");
        booking.Status.ShouldBe(BookingStatus.Confirmed);
    }

    [Fact]
    public void A_cancelled_booking_cannot_be_confirmed_by_a_late_payment()
    {
        var booking = NewBooking();
        booking.SeatsWereHeld(Details, Now.AddMinutes(3), Now);
        booking.Cancel("The seats were not paid for in time.", Now);

        booking.Confirm("AUTH-1", Now).IsFailure.ShouldBeTrue();
        booking.Status.ShouldBe(BookingStatus.Cancelled);
    }

    [Fact]
    public void Repeating_a_transition_is_a_no_op_rather_than_an_error()
    {
        var booking = NewBooking();
        booking.SeatsWereHeld(Details, Now.AddMinutes(3), Now);

        booking.SeatsWereHeld(Details, Now.AddMinutes(9), Now).IsSuccess.ShouldBeTrue();
        booking.PayBeforeUtc.ShouldBe(Now.AddMinutes(3));
    }
}
