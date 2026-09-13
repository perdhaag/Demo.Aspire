using Demo.Aspire.Bookings.Domain;
using Demo.Aspire.Contracts;
using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Bookings.Features.AwaitPayment;

/// <summary>Screenings said yes. Move the booking on and ask Payments for the money.</summary>
public sealed class SeatsHeldConsumer(
    IBookingRepository bookings,
    IUnitOfWork unitOfWork,
    CorrelationContext correlation,
    TimeProvider clock,
    ILogger<SeatsHeldConsumer> logger) : IConsumer<SeatsHeld>
{
    public async Task Consume(ConsumeContext<SeatsHeld> context)
    {
        var message = context.Message;
        correlation.Current = message.CorrelationId;

        var booking = await bookings.FindAsync(new BookingId(message.BookingId), context.CancellationToken);

        if (booking is null)
        {
            logger.LogError("Seats were held for unknown booking {BookingId}.", message.BookingId);
            return;
        }

        var details = new ScreeningDetails(message.FilmTitle, message.Auditorium, message.ScreeningStartsAtUtc);
        var result = booking.SeatsWereHeld(details, message.HoldExpiresAtUtc, clock.GetUtcNow());

        if (result.IsFailure)
        {
            logger.LogWarning("Booking {BookingId} ignored a seat hold: {Error}", message.BookingId, result.Error);
            return;
        }

        await unitOfWork.SaveChangesAsync(context.CancellationToken);
    }
}

internal sealed class PaymentRequestPublisher(
    IPublishEndpoint publishEndpoint,
    CorrelationContext correlation,
    TimeProvider clock) : IDomainEventHandler<BookingAwaitsPayment>
{
    public Task HandleAsync(BookingAwaitsPayment domainEvent, CancellationToken cancellationToken) =>
        publishEndpoint.Publish(
            new PaymentAuthorizationRequested(
                correlation.Current,
                domainEvent.BookingId.Value,
                domainEvent.Customer.Value,
                domainEvent.Total.Amount,
                domainEvent.Total.Currency,
                clock.GetUtcNow()),
            cancellationToken);
}
