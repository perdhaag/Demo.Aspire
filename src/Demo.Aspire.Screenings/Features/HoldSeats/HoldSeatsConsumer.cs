using Demo.Aspire.Contracts;
using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.Screenings.Domain;
using Demo.Aspire.Screenings.Features.HoldPolicy;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Screenings.Features.HoldSeats;

/// <summary>
/// The write side of the choreography. A booking was placed somewhere else in the
/// system; this context decides whether those seats can actually be had.
/// </summary>
public sealed class HoldSeatsConsumer(
    IScreeningRepository screenings,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint,
    CorrelationContext correlation,
    SeatHoldPolicy holdPolicy,
    TimeProvider clock,
    ILogger<HoldSeatsConsumer> logger) : IConsumer<BookingPlaced>
{
    public async Task Consume(ConsumeContext<BookingPlaced> context)
    {
        var message = context.Message;
        correlation.Current = message.CorrelationId;

        var booking = new BookingReference(message.BookingId);
        var screeningId = new ScreeningId(message.ScreeningId);
        var screening = await screenings.FindWithSeatsAsync(screeningId, context.CancellationToken);

        if (screening is null)
        {
            await RejectAsync(message, "The screening does not exist.", context.CancellationToken);
            return;
        }

        var requested = ParseSeats(message.Seats);

        if (requested.IsFailure)
        {
            await RejectAsync(message, requested.Error.Description, context.CancellationToken);
            return;
        }

        var hold = screening.HoldSeats(booking, requested.Value, clock.GetUtcNow(), holdPolicy.Duration);

        try
        {
            await unitOfWork.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException("optimistic concurrency — retrying", exception);
        }

        logger.LogInformation(
            "Booking {BookingId} on screening {ScreeningId}: {Outcome}",
            message.BookingId,
            message.ScreeningId,
            hold.IsSuccess ? $"holding {hold.Value.Seats.Count} seat(s) until {hold.Value.ExpiresAtUtc:HH:mm:ss}" : hold.Error.ToString());
    }

    /// <summary>
    /// Refuses a request that never reached the aggregate, so there is no domain event to
    /// translate. The publish lands in the outbox and is committed by the save below.
    /// </summary>
    private async Task RejectAsync(BookingPlaced message, string reason, CancellationToken cancellationToken)
    {
        logger.LogWarning("Refusing booking {BookingId}: {Reason}", message.BookingId, reason);

        await publishEndpoint.Publish(
            new SeatHoldRejected(
                message.CorrelationId,
                message.BookingId,
                message.ScreeningId,
                message.Seats,
                reason,
                clock.GetUtcNow()),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static Result<IReadOnlyCollection<SeatNumber>> ParseSeats(IReadOnlyList<string> seats)
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

/// <summary>Translates the aggregate's decision into the system's published language.</summary>
internal sealed class SeatHoldOutcomePublisher(
    IPublishEndpoint publishEndpoint,
    CorrelationContext correlation,
    TimeProvider clock) :
    IDomainEventHandler<SeatsHeldForBooking>,
    IDomainEventHandler<SeatHoldRefused>
{
    public Task HandleAsync(SeatsHeldForBooking domainEvent, CancellationToken cancellationToken) =>
        publishEndpoint.Publish(
            new SeatsHeld(
                correlation.Current,
                domainEvent.Booking.Value,
                domainEvent.ScreeningId.Value,
                domainEvent.FilmTitle,
                domainEvent.Auditorium,
                domainEvent.ScreeningStartsAtUtc,
                [.. domainEvent.Seats.Select(seat => seat.ToString())],
                domainEvent.HoldExpiresAtUtc,
                clock.GetUtcNow()),
            cancellationToken);

    public Task HandleAsync(SeatHoldRefused domainEvent, CancellationToken cancellationToken) =>
        publishEndpoint.Publish(
            new SeatHoldRejected(
                correlation.Current,
                domainEvent.Booking.Value,
                domainEvent.ScreeningId.Value,
                [.. domainEvent.Seats.Select(seat => seat.ToString())],
                domainEvent.Reason.Description,
                clock.GetUtcNow()),
            cancellationToken);
}
