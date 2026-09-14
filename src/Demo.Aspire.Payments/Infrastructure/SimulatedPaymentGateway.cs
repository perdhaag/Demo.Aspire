using System.Globalization;
using Demo.Aspire.Payments.Domain;
using Demo.Aspire.Payments.Features.Chaos;
using Demo.Aspire.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Demo.Aspire.Payments.Infrastructure;

public sealed class SimulatedPaymentGatewayOptions
{
    public const string SectionName = "PaymentGateway";

    /// <summary>Anything above this is treated as a suspicious amount and refused.</summary>
    public decimal DeclineAbove { get; set; } = 5_000m;

    /// <summary>How long the imaginary card network takes to answer.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(400);
}

/// <summary>
/// The adapter behind <see cref="IPaymentGateway"/>. It is deliberately predictable so
/// the demo can show both endings on purpose: book for an address whose local part
/// contains <c>decline</c>, or spend more than the configured ceiling, and the payment
/// is refused. Everything else is approved.
/// </summary>
internal sealed class SimulatedPaymentGateway(
    IOptions<SimulatedPaymentGatewayOptions> options,
    ChaosSwitch chaos,
    TimeProvider clock,
    ILogger<SimulatedPaymentGateway> logger) : IPaymentGateway
{
    private static readonly TimeSpan ChaosSlowLatency = TimeSpan.FromSeconds(5);

    public async Task<AuthorizationOutcome> AuthorizeAsync(
        EmailAddress payer,
        Money amount,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        await Task.Delay(chaos.IsSlow ? ChaosSlowLatency : settings.Latency, clock, cancellationToken);

        if (chaos.TryConsumeFailure())
        {
            logger.LogWarning("Chaos: simulating a card network failure for {Payer}.", payer);
            throw new InvalidOperationException("The card network refused the connection.");
        }

        if (payer.LocalPart.Contains("decline", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Declining {Amount} for {Payer}: test card.", amount, payer);
            return AuthorizationOutcome.Decline("The card was reported lost or stolen.");
        }

        if (amount.Amount > settings.DeclineAbove)
        {
            logger.LogInformation("Declining {Amount} for {Payer}: over the ceiling.", amount, payer);
            return AuthorizationOutcome.Decline(
                $"The amount exceeds the {settings.DeclineAbove:0} {amount.Currency} limit for this card.");
        }

        var reference = string.Create(
            CultureInfo.InvariantCulture,
            $"AUTH-{clock.GetUtcNow():yyyyMMdd}-{Guid.CreateVersion7().ToString("N")[..10].ToUpperInvariant()}");

        logger.LogInformation("Approved {Amount} for {Payer} as {Reference}.", amount, payer, reference);
        return AuthorizationOutcome.Approve(reference);
    }
}
