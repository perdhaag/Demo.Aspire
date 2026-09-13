using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Endpoints;
using Demo.Aspire.Platform.Http;
using Demo.Aspire.Screenings.Domain;
using Demo.Aspire.Screenings.Infrastructure;
using Demo.Aspire.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Demo.Aspire.Screenings.Features.GetSeatMap;

internal sealed class GetSeatMapHandler(
    IScreeningRepository screenings,
    SeatMapProjection projection,
    TimeProvider clock)
{
    public async Task<Result<SeatMapView>> HandleAsync(Guid screeningId, CancellationToken cancellationToken)
    {
        var id = new ScreeningId(screeningId);

        if (await projection.TryGetAsync(id, cancellationToken) is { } cached)
        {
            return cached;
        }

        var screening = await screenings.FindWithSeatsAsync(id, cancellationToken);

        return screening is null
            ? Error.NotFound("screening.unknown", $"No screening with id {screeningId}.")
            : await projection.StoreAsync(SeatMapProjection.Project(screening, clock.GetUtcNow()), cancellationToken);
    }
}

public sealed class GetSeatMapEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes
        .MapGet("/screenings/{screeningId:guid}/seats", async (
            Guid screeningId,
            GetSeatMapHandler handler,
            CancellationToken cancellationToken) =>
            (await handler.HandleAsync(screeningId, cancellationToken)).ToOk(view => view))
        .WithName("GetSeatMap")
        .WithSummary("Returns the seat map, served from Redis when it is warm.")
        .WithTags("Screenings");
}

/// <summary>
/// The slice that owns the cache owns invalidating it. Because this runs as a domain
/// event handler it fires for every path that changes seats &mdash; the HTTP one, the
/// message consumers and the expiry sweeper &mdash; without any of them knowing.
/// </summary>
internal sealed class SeatMapCacheInvalidator(SeatMapProjection projection) :
    IDomainEventHandler<SeatsHeldForBooking>,
    IDomainEventHandler<SeatsSold>,
    IDomainEventHandler<SeatsReleased>,
    IDomainEventHandler<SeatHoldLapsed>,
    IDomainEventHandler<ScreeningScheduled>
{
    public Task HandleAsync(SeatsHeldForBooking domainEvent, CancellationToken cancellationToken) =>
        projection.InvalidateAsync(domainEvent.ScreeningId, cancellationToken);

    public Task HandleAsync(SeatsSold domainEvent, CancellationToken cancellationToken) =>
        projection.InvalidateAsync(domainEvent.ScreeningId, cancellationToken);

    public Task HandleAsync(SeatsReleased domainEvent, CancellationToken cancellationToken) =>
        projection.InvalidateAsync(domainEvent.ScreeningId, cancellationToken);

    public Task HandleAsync(SeatHoldLapsed domainEvent, CancellationToken cancellationToken) =>
        projection.InvalidateAsync(domainEvent.ScreeningId, cancellationToken);

    public Task HandleAsync(ScreeningScheduled domainEvent, CancellationToken cancellationToken) =>
        projection.InvalidateAsync(domainEvent.ScreeningId, cancellationToken);
}
