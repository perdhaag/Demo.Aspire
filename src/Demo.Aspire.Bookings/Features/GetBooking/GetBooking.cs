using Demo.Aspire.Bookings.Domain;
using Demo.Aspire.Platform.Endpoints;
using Demo.Aspire.Platform.Http;
using Demo.Aspire.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Demo.Aspire.Bookings.Features.GetBooking;

public sealed record BookingResponse(
    Guid BookingId,
    Guid ScreeningId,
    string FilmTitle,
    string Auditorium,
    DateTimeOffset ScreeningStartsAtUtc,
    string CustomerEmail,
    IReadOnlyList<string> Seats,
    decimal Total,
    string Currency,
    string Status,
    DateTimeOffset PlacedAtUtc,
    DateTimeOffset? SeatsHeldAtUtc,
    DateTimeOffset? FinishedAtUtc,
    DateTimeOffset? PayBeforeUtc,
    string? PaymentReference,
    string? CancellationReason)
{
    public static BookingResponse From(Booking booking) => new(
        booking.Id.Value,
        booking.Screening.Value,
        booking.Details.FilmTitle,
        booking.Details.Auditorium,
        booking.Details.StartsAtUtc,
        booking.Customer.Value,
        [.. booking.Seats.Select(seat => seat.ToString())],
        booking.Total.Amount,
        booking.Total.Currency,
        booking.Status.ToString(),
        // The UI measures every later step against this, so the flow it draws is built
        // from the services' own clocks rather than from when the browser noticed.
        booking.PlacedAtUtc,
        booking.SeatsHeldAtUtc,
        booking.FinishedAtUtc,
        booking.PayBeforeUtc,
        booking.PaymentReference,
        booking.CancellationReason);
}

internal sealed class GetBookingHandler(IBookingRepository bookings)
{
    public async Task<Result<BookingResponse>> HandleAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await bookings.FindAsync(new BookingId(bookingId), cancellationToken);

        return booking is null
            ? Error.NotFound("booking.unknown", $"No booking with id {bookingId}.")
            : BookingResponse.From(booking);
    }
}

internal sealed class ListCustomerBookingsHandler(IBookingRepository bookings)
{
    public async Task<Result<IReadOnlyList<BookingResponse>>> HandleAsync(
        string customerEmail,
        CancellationToken cancellationToken)
    {
        var customer = EmailAddress.Create(customerEmail);

        if (customer.IsFailure)
        {
            return customer.Error;
        }

        var found = await bookings.FindForCustomerAsync(customer.Value, cancellationToken);
        return Result.Success<IReadOnlyList<BookingResponse>>([.. found.Select(BookingResponse.From)]);
    }
}

public sealed class GetBookingEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes)
    {
        // Polling this is how a client watches the flow finish, which is the honest
        // trade-off of choreography: the answer is eventual, not immediate.
        routes.MapGet("/bookings/{bookingId:guid}", async (
                Guid bookingId,
                GetBookingHandler handler,
                CancellationToken cancellationToken) =>
                (await handler.HandleAsync(bookingId, cancellationToken)).ToOk(response => response))
            .WithName("GetBooking")
            .WithSummary("Returns the current state of one booking.")
            .WithTags("Bookings");

        routes.MapGet("/bookings", async (
                string customerEmail,
                ListCustomerBookingsHandler handler,
                CancellationToken cancellationToken) =>
                (await handler.HandleAsync(customerEmail, cancellationToken)).ToOk(response => response))
            .WithName("ListCustomerBookings")
            .WithSummary("Returns every booking made by one customer.")
            .WithTags("Bookings");
    }
}
