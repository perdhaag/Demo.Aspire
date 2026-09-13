namespace Demo.Aspire.SharedKernel;

/// <summary>How a caller &mdash; ultimately an HTTP client &mdash; should read a failure.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unprocessable,
}

/// <summary>
/// A domain failure that the caller is expected to handle. Errors are values, not
/// exceptions: an unavailable seat is a normal business outcome, not a bug.
/// </summary>
public readonly record struct Error(string Code, string Description, ErrorKind Kind = ErrorKind.Validation)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Validation(string code, string description) => new(code, description);

    public static Error NotFound(string code, string description) => new(code, description, ErrorKind.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorKind.Conflict);

    public static Error Unprocessable(string code, string description) =>
        new(code, description, ErrorKind.Unprocessable);

    public override string ToString() => $"{Code}: {Description}";
}
