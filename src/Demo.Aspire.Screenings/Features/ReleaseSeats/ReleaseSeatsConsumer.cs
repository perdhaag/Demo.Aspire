using Demo.Aspire.Contracts;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.Screenings.Domain;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Screenings.Features.ReleaseSeats;

/// <summary>
/// The compensating action of the booking flow. Whatever went wrong downstream &mdash;
/// a declined card, an abandoned checkout &mdash; the seats must go back on sale.
/// </summary>
public sealed class ReleaseSeatsConsumer(
    IScreeningRepository screenings,
    IUnitOfWork unitOfWork,
    CorrelationContext correlation,
    ILogger<ReleaseSeatsConsumer> logger) : IConsumer<BookingCancelled>
{
    public async Task Consume(ConsumeContext<BookingCancelled> context)
    {
        var message = context.Message;
        correlation.Current = message.CorrelationId;

        var screening = await screenings.FindWithSeatsAsync(
            new ScreeningId(message.ScreeningId),
            context.CancellationToken);

        if (screening is null)
        {
            return;
        }

        screening.ReleaseSeats(new BookingReference(message.BookingId), message.Reason);
        await unitOfWork.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation(
            "Released seats held by cancelled booking {BookingId}: {Reason}",
            message.BookingId,
            message.Reason);
    }
}
