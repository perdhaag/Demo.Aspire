using Demo.Aspire.Payments.Domain;
using Demo.Aspire.Platform.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Demo.Aspire.Payments.Features.ListPayments;

public sealed record PaymentListItem(
    Guid PaymentId,
    Guid BookingId,
    string Payer,
    decimal Amount,
    string Currency,
    string Status,
    string? Reference,
    string? DeclineReason,
    DateTimeOffset DecidedAtUtc);

internal sealed class ListPaymentsHandler(IPaymentRepository payments)
{
    public async Task<IReadOnlyList<PaymentListItem>> HandleAsync(int take, CancellationToken cancellationToken) =>
    [
        .. (await payments.ListRecentAsync(Math.Clamp(take, 1, 200), cancellationToken))
            .Select(payment => new PaymentListItem(
                payment.Id.Value,
                payment.Booking.Value,
                payment.Payer.Value,
                payment.Amount.Amount,
                payment.Amount.Currency,
                payment.Status.ToString(),
                payment.Reference,
                payment.DeclineReason,
                payment.DecidedAtUtc)),
    ];
}

public sealed class ListPaymentsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes
        .MapGet("/payments", async (
            ListPaymentsHandler handler,
            CancellationToken cancellationToken,
            int take = 50) =>
            TypedResults.Ok(await handler.HandleAsync(take, cancellationToken)))
        .WithName("ListPayments")
        .WithSummary("Shows the most recent payment decisions, approved and declined.")
        .WithTags("Payments");
}
