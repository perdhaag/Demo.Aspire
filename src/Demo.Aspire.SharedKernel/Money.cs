using System.Globalization;

namespace Demo.Aspire.SharedKernel;

/// <summary>
/// A value object: immutable, compared by value, and self-validating. Money is never
/// a bare decimal here because "12.50" is meaningless without its currency.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public const string DefaultCurrency = "NOK";

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero(string currency = DefaultCurrency) => new(0m, currency);

    public static Result<Money> Create(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0)
        {
            return Error.Validation("money.negative", "An amount must not be negative.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            return Error.Validation("money.precision", "An amount must not have more than two decimals.");
        }

        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
        {
            return Error.Validation("money.currency", "Currency must be a three letter ISO-4217 code.");
        }

        return new Money(amount, currency);
    }

    public static Money From(decimal amount, string currency = DefaultCurrency) =>
        Create(amount, currency) is { IsSuccess: true } result
            ? result.Value
            : throw new ArgumentException($"{amount} {currency} is not a valid amount.", nameof(amount));

    public Money Times(int factor) =>
        factor < 0
            ? throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must not be negative.")
            : new Money(Amount * factor, Currency);

    public Money Add(Money other) => Currency == other.Currency
        ? new Money(Amount + other.Amount, Currency)
        : throw new InvalidOperationException($"Cannot add {other.Currency} to {Currency}.");

    public int CompareTo(Money other) => Currency == other.Currency
        ? Amount.CompareTo(other.Amount)
        : throw new InvalidOperationException($"Cannot compare {other.Currency} with {Currency}.");

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");
}
