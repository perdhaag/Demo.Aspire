using Demo.Aspire.Contracts;
using Demo.Aspire.Notifications.Infrastructure;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Notifications.Features.SendTicket;

/// <summary>
/// The last step of the happy path. This context has no opinion about bookings; it only
/// knows how to turn a fact that was published into something a person reads.
/// </summary>
public sealed class SendTicketConsumer(
    IEmailSender email,
    NotificationLog log,
    TimeProvider clock,
    ILogger<SendTicketConsumer> logger) : IConsumer<BookingConfirmed>
{
    public async Task Consume(ConsumeContext<BookingConfirmed> context)
    {
        var message = context.Message;
        var messageId = context.MessageId ?? message.BookingId;

        // Without a database there is no transactional inbox, so Redis is what stops a
        // redelivery from mailing the customer twice.
        if (!await log.TryClaimAsync(messageId))
        {
            logger.LogInformation("Ticket for booking {BookingId} was already sent.", message.BookingId);
            return;
        }

        var subject = $"Your tickets for {message.FilmTitle}";

        try
        {
            await email.SendAsync(
                new EmailMessage(
                    message.CustomerEmail,
                    subject,
                    TicketTemplate.Confirmation(
                        message.FilmTitle,
                        message.Auditorium,
                        message.ScreeningStartsAtUtc,
                        message.Seats,
                        message.Amount,
                        message.Currency,
                        message.PaymentReference,
                        message.BookingId)),
                context.CancellationToken);
        }
        catch
        {
            // Give the claim back so the broker's retry can actually retry.
            await log.ReleaseClaimAsync(messageId);
            throw;
        }

        await log.RecordAsync(new NotificationEntry(
            messageId,
            message.BookingId,
            message.CustomerEmail,
            subject,
            "Ticket sent",
            clock.GetUtcNow()));

        logger.LogInformation("Ticket mailed to {Recipient} for booking {BookingId}.",
            message.CustomerEmail, message.BookingId);
    }
}
