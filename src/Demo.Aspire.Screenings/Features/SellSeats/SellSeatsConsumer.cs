using Demo.Aspire.Contracts;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.Screenings.Domain;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Screenings.Features.SellSeats;

/// <summary>Payment cleared, so the hold becomes a sale. The seats are gone for good.</summary>
public sealed class SellSeatsConsumer(
    IScreeningRepository screenings,
    IUnitOfWork unitOfWork,
    CorrelationContext correlation,
    ILogger<SellSeatsConsumer> logger) : IConsumer<BookingConfirmed>
{
    public async Task Consume(ConsumeContext<BookingConfirmed> context)
    {
        var message = context.Message;
        correlation.Current = message.CorrelationId;

        var screening = await screenings.FindWithSeatsAsync(
            new ScreeningId(message.ScreeningId),
            context.CancellationToken);

        if (screening is null)
        {
            logger.LogError("Confirmed booking {BookingId} refers to unknown screening.", message.BookingId);
            return;
        }

        var result = screening.SellHeldSeats(new BookingReference(message.BookingId));

        if (result.IsFailure)
        {
            logger.LogWarning("Cannot sell seats for {BookingId}: {Error}", message.BookingId, result.Error);
            return;
        }

        await unitOfWork.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Seats sold for booking {BookingId}.", message.BookingId);
    }
}
