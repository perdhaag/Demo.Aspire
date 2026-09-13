using Demo.Aspire.SharedKernel;

namespace Demo.Aspire.Payments.Domain;

public readonly record struct PaymentId(Guid Value)
{
    public static PaymentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct BookingReference(Guid Value)
{
    public override string ToString() => Value.ToString();
}

public enum PaymentStatus
{
    Captured,
    Declined,
}

public sealed record PaymentWasCaptured(
    PaymentId PaymentId,
    BookingReference Booking,
    string Reference,
    Money Amount) : DomainEvent;

public sealed record PaymentWasDeclined(
    PaymentId PaymentId,
    BookingReference Booking,
    string Reason) : DomainEvent;

/// <summary>
/// One attempt to take money for one booking. A payment is decided exactly once: the
/// aggregate has no method that moves it from declined to captured, because in this
/// domain that would be a new attempt, not a change of mind.
/// </summary>
public sealed class Payment : AggregateRoot<PaymentId>
{
    private Payment(
        PaymentId id,
        BookingReference booking,
        EmailAddress payer,
        Money amount,
        PaymentStatus status,
        DateTimeOffset decidedAtUtc) : base(id)
    {
        Booking = booking;
        Payer = payer;
        Amount = amount;
        Status = status;
        DecidedAtUtc = decidedAtUtc;
    }

    /// <summary>Required by EF Core materialisation only.</summary>
    private Payment()
    {
    }

    public BookingReference Booking { get; private set; }

    public EmailAddress Payer { get; private set; }

    public Money Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset DecidedAtUtc { get; private set; }

    public string? Reference { get; private set; }

    public string? DeclineReason { get; private set; }

    public static Payment Capture(
        BookingReference booking,
        EmailAddress payer,
        Money amount,
        string reference,
        DateTimeOffset now)
    {
        var payment = new Payment(PaymentId.New(), booking, payer, amount, PaymentStatus.Captured, now)
        {
            Reference = Guard.AgainstNullOrWhiteSpace(reference),
        };

        payment.Raise(new PaymentWasCaptured(payment.Id, booking, reference, amount));
        return payment;
    }

    public static Payment Decline(
        BookingReference booking,
        EmailAddress payer,
        Money amount,
        string reason,
        DateTimeOffset now)
    {
        var payment = new Payment(PaymentId.New(), booking, payer, amount, PaymentStatus.Declined, now)
        {
            DeclineReason = Guard.AgainstNullOrWhiteSpace(reason),
        };

        payment.Raise(new PaymentWasDeclined(payment.Id, booking, reason));
        return payment;
    }
}

public interface IPaymentRepository : IRepository<Payment, PaymentId>
{
    Task<Payment?> FindForBookingAsync(BookingReference booking, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Payment>> ListRecentAsync(int take, CancellationToken cancellationToken = default);
}
