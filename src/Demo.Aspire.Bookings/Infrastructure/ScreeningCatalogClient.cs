using System.Net;
using System.Net.Http.Json;
using Demo.Aspire.Bookings.Domain;
using Demo.Aspire.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Bookings.Infrastructure;

/// <summary>What Bookings needs to know before it dares create a booking.</summary>
public readonly record struct ScreeningOffer(
    ScreeningDetails Details,
    Money TicketPrice,
    IReadOnlySet<SeatNumber> AvailableSeats);

public interface IScreeningCatalog
{
    Task<Result<ScreeningOffer>> FindOfferAsync(ScreeningReference screening, CancellationToken cancellationToken);
}

/// <summary>
/// An anti-corruption layer. Screenings publishes a seat map shaped for a seat picker;
/// this class is the only place in Bookings that has ever heard of that shape, and it
/// hands the rest of the context value objects it already understands.
/// </summary>
/// <remarks>
/// The answer is advisory. Seats can be taken between this call and the message that
/// actually asks for them, which is why the real decision is still made by the
/// Screenings aggregate. Asking first just lets the customer be told immediately.
/// </remarks>
internal sealed class ScreeningCatalogClient(HttpClient httpClient, ILogger<ScreeningCatalogClient> logger)
    : IScreeningCatalog
{
    public async Task<Result<ScreeningOffer>> FindOfferAsync(
        ScreeningReference screening,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"/screenings/{screening.Value}/seats",
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return Error.NotFound("screening.unknown", $"No screening with id {screening.Value}.");
        }

        response.EnsureSuccessStatusCode();

        var seatMap = await response.Content.ReadFromJsonAsync<SeatMapContract>(cancellationToken);

        if (seatMap is null)
        {
            logger.LogError("Screenings returned an empty seat map for {ScreeningId}.", screening);
            return Error.Unprocessable("screening.unreadable", "The seat map could not be read.");
        }

        var price = Money.Create(seatMap.TicketPrice, seatMap.Currency);

        if (price.IsFailure)
        {
            return price.Error;
        }

        return new ScreeningOffer(
            new ScreeningDetails(seatMap.FilmTitle, seatMap.Auditorium, seatMap.StartsAtUtc),
            price.Value,
            seatMap.Seats
                .Where(seat => seat.Status == "Available")
                .Select(seat => SeatNumber.Parse(seat.Number))
                .Where(seat => seat.IsSuccess)
                .Select(seat => seat.Value)
                .ToHashSet());
    }

    private sealed record SeatMapContract(
        Guid ScreeningId,
        string FilmTitle,
        string Auditorium,
        DateTimeOffset StartsAtUtc,
        decimal TicketPrice,
        string Currency,
        int AvailableSeats,
        IReadOnlyList<SeatContract> Seats);

    private sealed record SeatContract(string Number, string Status);
}
