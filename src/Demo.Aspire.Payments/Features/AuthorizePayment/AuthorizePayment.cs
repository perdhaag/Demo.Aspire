using Demo.Aspire.Contracts;
using Demo.Aspire.Payments.Domain;
using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Payments.Features.AuthorizePayment;

/// <summary>
/// Takes the money, or explains why it could not. Charging twice is the failure mode
/// that matters here, so the slice checks for an existing decision before it calls the
/// gateway, on top of the inbox de-duplication the transport already provides.
/// </summary>
public sealed class AuthorizePaymentConsumer(
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IPaymentGateway gateway,
    CorrelationContext correlation,
    TimeProvider clock,
    ILogger<AuthorizePaymentConsumer> logger) : IConsumer<PaymentAuthorizationRequested>
{
    public async Task Consume(ConsumeContext<PaymentAuthorizationRequested> context)
    {
        var message = context.Message;
        correlation.Current = message.CorrelationId;

        var booking = new BookingReference(message.BookingId);
        var existing = await payments.FindForBookingAsync(booking, context.CancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "Booking {BookingId} was already {Status}; not charging again.",
                message.BookingId,
                existing.Status);
            return;
        }

        var payer = EmailAddress.Create(message.CustomerEmail);
        var amount = Money.Create(message.Amount, message.Currency);

        if (payer.IsFailure || amount.IsFailure)
        {
            var reason = payer.IsFailure ? payer.Error.Description : amount.Error.Description;
            await RecordAsync(Payment.Decline(booking, Fallback(payer), Money.Zero(), reason, clock.GetUtcNow()),
                context.CancellationToken);
            return;
        }

        var outcome = await gateway.AuthorizeAsync(payer.Value, amount.Value, context.CancellationToken);

        var payment = outcome.Approved
            ? Payment.Capture(booking, payer.Value, amount.Value, outcome.Reference, clock.GetUtcNow())
            : Payment.Decline(booking, payer.Value, amount.Value, outcome.DeclineReason!, clock.GetUtcNow());

        await RecordAsync(payment, context.CancellationToken);
    }

    private async Task RecordAsync(Payment payment, CancellationToken cancellationToken)
    {
        await payments.AddAsync(payment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static EmailAddress Fallback(Result<EmailAddress> payer) =>
        payer.IsSuccess ? payer.Value : EmailAddress.Create("unknown@example.invalid").Value;
}

internal sealed class PaymentOutcomePublisher(
    IPublishEndpoint publishEndpoint,
    CorrelationContext correlation,
    TimeProvider clock) :
    IDomainEventHandler<PaymentWasCaptured>,
    IDomainEventHandler<PaymentWasDeclined>
{
    public Task HandleAsync(PaymentWasCaptured domainEvent, CancellationToken cancellationToken) =>
        publishEndpoint.Publish(
            new PaymentCaptured(
                correlation.Current,
                domainEvent.Booking.Value,
                domainEvent.PaymentId.Value,
                domainEvent.Reference,
                domainEvent.Amount.Amount,
                domainEvent.Amount.Currency,
                clock.GetUtcNow()),
            cancellationToken);

    public Task HandleAsync(PaymentWasDeclined domainEvent, CancellationToken cancellationToken) =>
        publishEndpoint.Publish(
            new PaymentDeclined(
                correlation.Current,
                domainEvent.Booking.Value,
                domainEvent.PaymentId.Value,
                domainEvent.Reason,
                clock.GetUtcNow()),
            cancellationToken);
}
