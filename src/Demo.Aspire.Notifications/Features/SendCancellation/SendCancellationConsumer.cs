using Demo.Aspire.Contracts;
using Demo.Aspire.Notifications.Infrastructure;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Notifications.Features.SendCancellation;

/// <summary>Tells the customer the booking failed, and that they were not charged.</summary>
public sealed class SendCancellationConsumer(
    IEmailSender email,
    NotificationLog log,
    TimeProvider clock,
    ILogger<SendCancellationConsumer> logger) : IConsumer<BookingCancelled>
{
    public async Task Consume(ConsumeContext<BookingCancelled> context)
    {
        var message = context.Message;
        var messageId = context.MessageId ?? message.BookingId;

        if (!await log.TryClaimAsync(messageId))
        {
            return;
        }

        const string Subject = "Your Demo Kino booking could not be completed";

        try
        {
            await email.SendAsync(
                new EmailMessage(
                    message.CustomerEmail,
                    Subject,
                    TicketTemplate.Cancellation(message.Seats, message.Reason, message.BookingId)),
                context.CancellationToken);
        }
        catch
        {
            await log.ReleaseClaimAsync(messageId);
            throw;
        }

        await log.RecordAsync(new NotificationEntry(
            messageId,
            message.BookingId,
            message.CustomerEmail,
            Subject,
            $"Cancellation sent: {message.Reason}",
            clock.GetUtcNow()));

        logger.LogInformation("Cancellation mailed to {Recipient}.", message.CustomerEmail);
    }
}
