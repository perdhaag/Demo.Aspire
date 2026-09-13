using Demo.Aspire.SharedKernel;

namespace Demo.Aspire.Payments.Domain;

/// <summary>The outcome a card network gives us, expressed in this domain's terms.</summary>
public readonly record struct AuthorizationOutcome(bool Approved, string Reference, string? DeclineReason)
{
    public static AuthorizationOutcome Approve(string reference) => new(true, reference, null);

    public static AuthorizationOutcome Decline(string reason) => new(false, string.Empty, reason);
}

/// <summary>
/// A port. The domain says what it needs from a payment provider; the adapter in
/// Infrastructure decides whether that is Stripe, Nets, or the simulator this demo uses.
/// </summary>
public interface IPaymentGateway
{
    Task<AuthorizationOutcome> AuthorizeAsync(
        EmailAddress payer,
        Money amount,
        CancellationToken cancellationToken);
}
