using System.Text.RegularExpressions;

namespace Demo.Aspire.SharedKernel;

/// <summary>A validated e-mail address. Invalid instances cannot be constructed.</summary>
public readonly partial record struct EmailAddress
{
    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public string LocalPart => Value[..Value.IndexOf('@')];

    public static Result<EmailAddress> Create(string? value)
    {
        var candidate = value?.Trim();

        if (string.IsNullOrEmpty(candidate))
        {
            return Error.Validation("email.empty", "An e-mail address is required.");
        }

        return Pattern().IsMatch(candidate)
            ? new EmailAddress(candidate.ToLowerInvariant())
            : Error.Validation("email.invalid", $"'{candidate}' is not a valid e-mail address.");
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[^@\\s]+@[^@\\s.]+(\\.[^@\\s.]+)+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Pattern();
}
