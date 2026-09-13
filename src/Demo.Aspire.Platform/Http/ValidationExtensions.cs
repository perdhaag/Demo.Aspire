using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace Demo.Aspire.Platform.Http;

public static class ValidationExtensions
{
    extension<TRequest>(IValidator<TRequest> validator)
    {
        /// <summary>
        /// Shape-checks a request at the edge. This is deliberately separate from the
        /// domain rules inside the aggregate: "seatsPerRow must be a number" is an input
        /// concern, "this seat is taken" is a business decision.
        /// </summary>
        public async Task<IResult?> TryValidateAsync(TRequest request, CancellationToken cancellationToken)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            return result.IsValid ? null : TypedResults.ValidationProblem(result.ToProblemDictionary());
        }
    }

    extension(ValidationResult result)
    {
        public IDictionary<string, string[]> ToProblemDictionary() => result.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray());
    }
}
