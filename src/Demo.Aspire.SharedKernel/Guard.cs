using System.Runtime.CompilerServices;

namespace Demo.Aspire.SharedKernel;

/// <summary>
/// Guards protect invariants that should be impossible to break through the public
/// API. Unlike <see cref="Result"/>, hitting one of these means a programming error,
/// so it throws.
/// </summary>
public static class Guard
{
    public static T AgainstNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class =>
        value ?? throw new ArgumentNullException(name);

    public static string AgainstNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? name = null) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;

    public static decimal AgainstNegative(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? name = null) =>
        value < 0 ? throw new ArgumentOutOfRangeException(name, value, "Value must not be negative.") : value;
}
