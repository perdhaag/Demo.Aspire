using Demo.Aspire.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Demo.Aspire.Platform.Http;

/// <summary>
/// Translates domain <see cref="Error"/> values into RFC 9457 problem details, so the
/// slices themselves never have to think in status codes.
/// </summary>
public static class ResultExtensions
{
    extension(Error error)
    {
        public int StatusCode => error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest,
        };

        public ProblemHttpResult ToProblem() => TypedResults.Problem(
            detail: error.Description,
            statusCode: error.StatusCode,
            title: error.Kind.ToString(),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    extension<TValue>(Result<TValue> result)
    {
        /// <summary>200 with the mapped payload, or a problem document.</summary>
        public IResult ToOk<TResponse>(Func<TValue, TResponse> map) =>
            result.IsSuccess ? TypedResults.Ok(map(result.Value)) : result.Error.ToProblem();

        /// <summary>202 with the mapped payload, or a problem document.</summary>
        public IResult ToAccepted<TResponse>(Func<TValue, string> location, Func<TValue, TResponse> map) =>
            result.IsSuccess
                ? TypedResults.Accepted(location(result.Value), map(result.Value))
                : result.Error.ToProblem();
    }
}
