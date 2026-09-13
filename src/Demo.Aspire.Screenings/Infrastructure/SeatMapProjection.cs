using System.Text.Json;
using System.Text.Json.Serialization;
using Demo.Aspire.Screenings.Domain;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Screenings.Infrastructure;

/// <summary>A read model. Shaped for the seat picker, not for the domain.</summary>
public sealed record SeatMapView(
    Guid ScreeningId,
    string FilmTitle,
    string Auditorium,
    DateTimeOffset StartsAtUtc,
    decimal TicketPrice,
    string Currency,
    int AvailableSeats,
    IReadOnlyList<SeatView> Seats);

public sealed record SeatView(string Number, string Status);

[JsonSerializable(typeof(SeatMapView))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class SeatMapJsonContext : JsonSerializerContext;

/// <summary>
/// The seat picker is read far more often than seats change, and every reader wants the
/// same answer. That is exactly what Redis is for: the projection is cached across all
/// instances of the service and dropped the moment the aggregate changes.
/// </summary>
public sealed class SeatMapProjection(IDistributedCache cache, ILogger<SeatMapProjection> logger)
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        // Short enough that a missed invalidation self-heals, long enough to matter.
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    public async Task<SeatMapView?> TryGetAsync(ScreeningId screeningId, CancellationToken cancellationToken)
    {
        var cached = await cache.GetStringAsync(KeyFor(screeningId), cancellationToken);

        if (cached is null)
        {
            return null;
        }

        logger.LogDebug("Seat map for {ScreeningId} served from Redis.", screeningId);
        return JsonSerializer.Deserialize(cached, SeatMapJsonContext.Default.SeatMapView);
    }

    public async Task<SeatMapView> StoreAsync(SeatMapView view, CancellationToken cancellationToken)
    {
        await cache.SetStringAsync(
            KeyFor(new ScreeningId(view.ScreeningId)),
            JsonSerializer.Serialize(view, SeatMapJsonContext.Default.SeatMapView),
            CacheOptions,
            cancellationToken);

        return view;
    }

    public Task InvalidateAsync(ScreeningId screeningId, CancellationToken cancellationToken)
    {
        logger.LogDebug("Dropping cached seat map for {ScreeningId}.", screeningId);
        return cache.RemoveAsync(KeyFor(screeningId), cancellationToken);
    }

    public static SeatMapView Project(Screening screening, DateTimeOffset now) => new(
        screening.Id.Value,
        screening.FilmTitle,
        screening.Auditorium,
        screening.StartsAtUtc,
        screening.TicketPrice.Amount,
        screening.TicketPrice.Currency,
        screening.AvailableSeatCountAt(now),
        [
            .. screening.Seats
                .OrderBy(seat => seat.Number)
                .Select(seat => new SeatView(
                    seat.Number.ToString(),
                    seat.IsAvailableAt(now) ? nameof(SeatStatus.Available) : seat.Status.ToString())),
        ]);

    private static string KeyFor(ScreeningId screeningId) => $"screenings:seat-map:{screeningId.Value}";
}
