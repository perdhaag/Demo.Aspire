using Demo.Aspire.Bookings.Domain;
using Demo.Aspire.Contracts;
using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Bookings.Features.ConfirmBooking;

/// <summary>The happy ending: money in, ticket out.</summary>
public sealed class PaymentCapturedConsumer(
    IBookingRepository bookings,
    IUnitOfWork unitOfWork,
    CorrelationContext correlation,
    TimeProvider clock,
    ILogger<PaymentCapturedConsumer> logger) : IConsumer<PaymentCaptured>
{
    public async Task Consume(ConsumeContext<PaymentCaptured> context)
    {
        var message = context.Message;
        correlation.Current = message.CorrelationId;

        var booking = await bookings.FindAsync(new BookingId(message.BookingId), context.CancellationToken);

        if (booking is null)
        {
            logger.LogError("Captured payment {PaymentId} for unknown booking.", message.PaymentId);
            return;
        }

        var result = booking.Confirm(message.PaymentReference, clock.GetUtcNow());

        if (result.IsFailure)
        {
            // The hold lapsed while the card was being charged. The booking stays
            // cancelled and a refund is owed: a real system would raise that here.
            logger.LogWarning(
                "Payment {PaymentReference} arrived too late for booking {BookingId}: {Error}",
                message.PaymentReference,
                message.BookingId,
                result.Error);
            return;
        }

        await unitOfWork.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Booking {BookingId} confirmed.", message.BookingId);
    }
}

internal sealed class BookingConfirmedPublisher(
    IPublishEndpoint publishEndpoint,
    CorrelationContext correlation,
    TimeProvider clock) : IDomainEventHandler<BookingWasConfirmed>
{
    public Task HandleAsync(BookingWasConfirmed domainEvent, CancellationToken cancellationToken) =>
        publishEndpoint.Publish(
            new BookingConfirmed(
                correlation.Current,
                domainEvent.BookingId.Value,
                domainEvent.Screening.Value,
                domainEvent.Details.FilmTitle,
                domainEvent.Details.Auditorium,
                domainEvent.Details.StartsAtUtc,
                domainEvent.Customer.Value,
                [.. domainEvent.Seats.Select(seat => seat.ToString())],
                domainEvent.Total.Amount,
                domainEvent.Total.Currency,
                domainEvent.PaymentReference,
                clock.GetUtcNow()),
            cancellationToken);
}
