using Demo.Aspire.SharedKernel;
using Shouldly;

namespace Demo.Aspire.Tests;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("  Ada@Example.COM ")]
    public void Valid_addresses_are_normalised(string input) =>
        EmailAddress.Create(input).Value.Value.ShouldBe("ada@example.com");

    [Theory]
    [InlineData("")]
    [InlineData("ada")]
    [InlineData("ada@localhost")]
    [InlineData("ada@@example.com")]
    public void Invalid_addresses_are_refused(string input) =>
        EmailAddress.Create(input).IsFailure.ShouldBeTrue();

    [Fact]
    public void Money_refuses_fractions_of_an_øre() =>
        Money.Create(10.001m).Error.Code.ShouldBe("money.precision");

    [Fact]
    public void Money_refuses_to_mix_currencies() =>
        Should.Throw<InvalidOperationException>(() => Money.From(10m, "NOK").Add(Money.From(10m, "EUR")));

    [Fact]
    public void Money_multiplies_by_a_seat_count() =>
        Money.From(199m).Times(3).ShouldBe(Money.From(597m));

    [Theory]
    [InlineData("c12", 'C', 12)]
    [InlineData("A1", 'A', 1)]
    public void Seat_numbers_round_trip(string input, char row, int number)
    {
        var seat = SeatNumber.Parse(input).Value;

        seat.Row.ShouldBe(row);
        seat.Number.ShouldBe(number);
        seat.ToString().ShouldBe($"{row}{number}");
    }

    [Theory]
    [InlineData("12")]
    [InlineData("AA")]
    [InlineData("A0")]
    [InlineData("A100")]
    public void Nonsense_seat_numbers_are_refused(string input) =>
        SeatNumber.Parse(input).IsFailure.ShouldBeTrue();

    [Fact]
    public void Seats_sort_by_row_then_number()
    {
        SeatNumber[] seats =
        [
            SeatNumber.Parse("B2").Value,
            SeatNumber.Parse("A10").Value,
            SeatNumber.Parse("A2").Value,
        ];

        seats.Order().Select(seat => seat.ToString()).ShouldBe(["A2", "A10", "B2"]);
    }
}
