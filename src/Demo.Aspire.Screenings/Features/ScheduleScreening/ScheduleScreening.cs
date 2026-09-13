using Demo.Aspire.Platform.Endpoints;
using Demo.Aspire.Platform.Http;
using Demo.Aspire.Screenings.Domain;
using Demo.Aspire.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Demo.Aspire.Screenings.Features.ScheduleScreening;

// A vertical slice: the request, its input rules, the behaviour and the HTTP surface
// for one capability, in one file. Adding a feature never means editing another one.

public sealed record ScheduleScreeningRequest(
    string FilmTitle,
    string Auditorium,
    DateTimeOffset StartsAtUtc,
    decimal TicketPrice,
    string Currency,
    int Rows,
    int SeatsPerRow);

public sealed record ScheduledScreeningResponse(
    Guid Id,
    string FilmTitle,
    string Auditorium,
    DateTimeOffset StartsAtUtc,
    int SeatCount);

internal sealed class ScheduleScreeningValidator : AbstractValidator<ScheduleScreeningRequest>
{
    public ScheduleScreeningValidator()
    {
        RuleFor(request => request.FilmTitle).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Auditorium).NotEmpty().MaximumLength(100);
        RuleFor(request => request.TicketPrice).GreaterThan(0m);
        RuleFor(request => request.Currency).NotEmpty().Length(3);
        RuleFor(request => request.Rows).InclusiveBetween(1, 26);
        RuleFor(request => request.SeatsPerRow).InclusiveBetween(1, 99);
    }
}

internal sealed class ScheduleScreeningHandler(
    IScreeningRepository screenings,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<Result<ScheduledScreeningResponse>> HandleAsync(
        ScheduleScreeningRequest request,
        CancellationToken cancellationToken)
    {
        var price = Money.Create(request.TicketPrice, request.Currency.ToUpperInvariant());

        if (price.IsFailure)
        {
            return price.Error;
        }

        var screening = Screening.Schedule(
            request.FilmTitle,
            request.Auditorium,
            request.StartsAtUtc.ToUniversalTime(),
            price.Value,
            request.Rows,
            request.SeatsPerRow,
            clock.GetUtcNow());

        if (screening.IsFailure)
        {
            return screening.Error;
        }

        await screenings.AddAsync(screening.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ScheduledScreeningResponse(
            screening.Value.Id.Value,
            screening.Value.FilmTitle,
            screening.Value.Auditorium,
            screening.Value.StartsAtUtc,
            screening.Value.Seats.Count);
    }
}

public sealed class ScheduleScreeningEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes
        .MapPost("/screenings", async (
            ScheduleScreeningRequest request,
            IValidator<ScheduleScreeningRequest> validator,
            ScheduleScreeningHandler handler,
            CancellationToken cancellationToken) =>
            await validator.TryValidateAsync(request, cancellationToken)
            ?? (await handler.HandleAsync(request, cancellationToken))
            .Match(
                response => Results.Created($"/screenings/{response.Id}", response),
                error => error.ToProblem()))
        .WithName("ScheduleScreening")
        .WithSummary("Puts a new screening on sale, generating its seat map.")
        .WithTags("Screenings");
}
