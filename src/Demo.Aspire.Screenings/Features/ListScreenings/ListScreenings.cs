using Demo.Aspire.Platform.Endpoints;
using Demo.Aspire.Screenings.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Demo.Aspire.Screenings.Features.ListScreenings;

public sealed record ScreeningListItem(
    Guid Id,
    string FilmTitle,
    string Auditorium,
    DateTimeOffset StartsAtUtc,
    decimal TicketPrice,
    string Currency,
    int TotalSeats,
    int AvailableSeats);

/// <summary>
/// A query slice. It reads through the DbContext directly rather than through the
/// repository: there is no invariant to protect on the way out, and forcing every read
/// through the aggregate would only cost us seat-by-seat materialisation.
/// </summary>
internal sealed class ListScreeningsHandler(ScreeningsDbContext dbContext, TimeProvider clock)
{
    public async Task<IReadOnlyList<ScreeningListItem>> HandleAsync(
        bool includePast,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        return await dbContext.Screenings
            .AsNoTracking()
            .Where(screening => includePast || screening.StartsAtUtc > now)
            .OrderBy(screening => screening.StartsAtUtc)
            .Select(screening => new ScreeningListItem(
                screening.Id.Value,
                screening.FilmTitle,
                screening.Auditorium,
                screening.StartsAtUtc,
                screening.TicketPrice.Amount,
                screening.TicketPrice.Currency,
                screening.Seats.Count,
                screening.Seats.Count(seat =>
                    seat.Status == Domain.SeatStatus.Available
                    || (seat.Status == Domain.SeatStatus.Held && seat.HoldExpiresAtUtc <= now))))
            .ToListAsync(cancellationToken);
    }
}

public sealed class ListScreeningsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes
        .MapGet("/screenings", async (
            ListScreeningsHandler handler,
            CancellationToken cancellationToken,
            bool includePast = false) =>
            TypedResults.Ok(await handler.HandleAsync(includePast, cancellationToken)))
        .WithName("ListScreenings")
        .WithSummary("Lists screenings that are still open for booking.")
        .WithTags("Screenings")
        .CacheOutput(policy => policy.Expire(TimeSpan.FromSeconds(10)).SetVaryByQuery("includePast"));
}
