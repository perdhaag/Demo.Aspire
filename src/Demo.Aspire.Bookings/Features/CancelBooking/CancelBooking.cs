using Demo.Aspire.Bookings.Domain;
using Demo.Aspire.Contracts;
using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Bookings.Features.CancelBooking;

/// <summary>
/// Every unhappy path in the flow ends here. Three different things can go wrong in
/// three different services, and all of them mean the same thing to this context, so
/// one slice handles all three.
/// </summary>
public sealed class CancelBookingConsumer(
    IBookingRepository bookings,
    IUnitOfWork unitOfWork,
    CorrelationContext correlation,
    TimeProvider clock,
    ILogger<CancelBookingConsumer> logger) :
    IConsumer<SeatHoldRejected>,
    IConsumer<SeatHoldExpired>,
    IConsumer<PaymentDeclined>
{
    public Task Consume(ConsumeContext<SeatHoldRejected> context) => CancelAsync(
        context.Message.CorrelationId,
        context.Message.BookingId,
        $"The seats could not be held: {context.Message.Reason}",
        context.CancellationToken);

    public Task Consume(ConsumeContext<SeatHoldExpired> context) => CancelAsync(
        context.Message.CorrelationId,
        context.Message.BookingId,
        "The seats were not paid for in time.",
        context.CancellationToken);

    public Task Consume(ConsumeContext<PaymentDeclined> context) => CancelAsync(
        context.Message.CorrelationId,
        context.Message.BookingId,
        $"The payment was declined: {context.Message.Reason}",
        context.CancellationToken);

    private async Task CancelAsync(
        Guid correlationId,
        Guid bookingId,
        string reason,
        CancellationToken cancellationToken)
    {
        correlation.Current = correlationId;

        var booking = await bookings.FindAsync(new BookingId(bookingId), cancellationToken);

        if (booking is null)
        {
            logger.LogError("Cannot cancel unknown booking {BookingId}.", bookingId);
            return;
        }

        var result = booking.Cancel(reason, clock.GetUtcNow());

        if (result.IsFailure)
        {
            logger.LogWarning("Booking {BookingId} refused cancellation: {Error}", bookingId, result.Error);
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Booking {BookingId} cancelled: {Reason}", bookingId, reason);
    }
}

internal sealed class BookingCancelledPublisher(
    IPublishEndpoint publishEndpoint,
    CorrelationContext correlation,
    TimeProvider clock) : IDomainEventHandler<BookingWasCancelled>
{
    public Task HandleAsync(BookingWasCancelled domainEvent, CancellationToken cancellationToken) =>
        publishEndpoint.Publish(
            new BookingCancelled(
                correlation.Current,
                domainEvent.BookingId.Value,
                domainEvent.Screening.Value,
                domainEvent.Customer.Value,
                [.. domainEvent.Seats.Select(seat => seat.ToString())],
                domainEvent.Reason,
                clock.GetUtcNow()),
            cancellationToken);
}
