using Demo.Aspire.Bookings.Domain;
using Demo.Aspire.Bookings.Infrastructure;
using Demo.Aspire.Contracts;
using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Endpoints;
using Demo.Aspire.Platform.Http;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.SharedKernel;
using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Demo.Aspire.Bookings.Features.PlaceBooking;

public sealed record PlaceBookingRequest(Guid ScreeningId, string CustomerEmail, IReadOnlyList<string> Seats);

public sealed record PlacedBookingResponse(
    Guid BookingId,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyList<string> Seats);

internal sealed class PlaceBookingValidator : AbstractValidator<PlaceBookingRequest>
{
    public PlaceBookingValidator()
    {
        RuleFor(request => request.ScreeningId).NotEmpty();
        RuleFor(request => request.CustomerEmail).NotEmpty().MaximumLength(256);
        RuleFor(request => request.Seats).NotEmpty().Must(seats => seats.Count <= 8)
            .WithMessage("A booking may hold at most 8 seats.");
    }
}

/// <summary>
/// Creates the booking and lets go. Everything that follows &mdash; holding seats,
/// taking the money, sending the ticket &mdash; happens on the bus, so the customer
/// gets an answer in one round trip instead of waiting for three services in a row.
/// </summary>
internal sealed class PlaceBookingHandler(
    IBookingRepository bookings,
    IUnitOfWork unitOfWork,
    IScreeningCatalog catalog,
    CorrelationContext correlation,
    TimeProvider clock)
{
    public async Task<Result<PlacedBookingResponse>> HandleAsync(
        PlaceBookingRequest request,
        CancellationToken cancellationToken)
    {
        var customer = EmailAddress.Create(request.CustomerEmail);

        if (customer.IsFailure)
        {
            return customer.Error;
        }

        var seats = ParseSeats(request.Seats);

        if (seats.IsFailure)
        {
            return seats.Error;
        }

        var screening = new ScreeningReference(request.ScreeningId);
        var offer = await catalog.FindOfferAsync(screening, cancellationToken);

        if (offer.IsFailure)
        {
            return offer.Error;
        }

        SeatNumber[] unavailable = [.. seats.Value.Where(seat => !offer.Value.AvailableSeats.Contains(seat))];

        if (unavailable.Length > 0)
        {
            return Error.Conflict(
                "booking.seats-unavailable",
                $"Seat(s) {string.Join(", ", unavailable)} are not available.");
        }

        var booking = Booking.Place(screening, customer.Value, seats.Value, offer.Value.TicketPrice, clock.GetUtcNow());

        if (booking.IsFailure)
        {
            return booking.Error;
        }

        correlation.Current = booking.Value.Id.Value;

        await bookings.AddAsync(booking.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new PlacedBookingResponse(
            booking.Value.Id.Value,
            booking.Value.Status.ToString(),
            booking.Value.Total.Amount,
            booking.Value.Total.Currency,
            [.. booking.Value.Seats.Select(seat => seat.ToString())]);
    }

    private static Result<IReadOnlyList<SeatNumber>> ParseSeats(IReadOnlyList<string> seats)
    {
        List<SeatNumber> parsed = new(seats.Count);

        foreach (var text in seats)
        {
            var seat = SeatNumber.Parse(text);

            if (seat.IsFailure)
            {
                return seat.Error;
            }

            parsed.Add(seat.Value);
        }

        return parsed;
    }
}

public sealed class PlaceBookingEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes
        .MapPost("/bookings", async (
            PlaceBookingRequest request,
            IValidator<PlaceBookingRequest> validator,
            PlaceBookingHandler handler,
            CancellationToken cancellationToken) =>
            await validator.TryValidateAsync(request, cancellationToken)
            ?? (await handler.HandleAsync(request, cancellationToken))
            .ToAccepted(response => $"/bookings/{response.BookingId}", response => response))
        .WithName("PlaceBooking")
        .WithSummary("Starts a booking. The result is 202: the flow finishes on the bus.")
        .WithTags("Bookings");
}

internal sealed class BookingWasPlacedPublisher(
    IPublishEndpoint publishEndpoint,
    CorrelationContext correlation,
    TimeProvider clock) : IDomainEventHandler<BookingWasPlaced>
{
    public Task HandleAsync(BookingWasPlaced domainEvent, CancellationToken cancellationToken) =>
        publishEndpoint.Publish(
            new BookingPlaced(
                correlation.Current,
                domainEvent.BookingId.Value,
                domainEvent.Screening.Value,
                domainEvent.Customer.Value,
                [.. domainEvent.Seats.Select(seat => seat.ToString())],
                domainEvent.Total.Amount,
                domainEvent.Total.Currency,
                clock.GetUtcNow()),
            cancellationToken);
}
