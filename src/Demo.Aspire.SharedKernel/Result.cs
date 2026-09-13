using System.Diagnostics.CodeAnalysis;

namespace Demo.Aspire.SharedKernel;

/// <summary>Outcome of an operation that can fail for business reasons.</summary>
public readonly struct Result
{
    private Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    [MemberNotNullWhen(false, nameof(FailureReason))]
    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public string? FailureReason => IsSuccess ? null : Error.Description;

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>Returns the first failure in <paramref name="results"/>, or success.</summary>
    public static Result FirstFailureOr(params ReadOnlySpan<Result> results)
    {
        foreach (var result in results)
        {
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Success();
    }
}

/// <summary>Outcome of an operation that produces a value or fails for business reasons.</summary>
public readonly struct Result<TValue>
{
    private readonly TValue? _value;

    private Result(bool isSuccess, TValue? value, Error error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error}).");

    public static Result<TValue> Success(TValue value) => new(true, value, Error.None);

    public static Result<TValue> Failure(Error error) => new(false, default, error);

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure(error);

    public Result<TNext> Map<TNext>(Func<TValue, TNext> map) =>
        IsSuccess ? Result<TNext>.Success(map(Value)) : Result<TNext>.Failure(Error);

    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);
}
