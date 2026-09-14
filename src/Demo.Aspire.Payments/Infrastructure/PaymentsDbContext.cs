using Demo.Aspire.Payments.Domain;
using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Persistence;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Demo.Aspire.Payments.Infrastructure;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Id)
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .ValueGeneratedNever();

        builder.Property(payment => payment.Booking)
            .HasConversion(reference => reference.Value, value => new BookingReference(value))
            .IsRequired();

        builder.Property(payment => payment.Payer)
            .HasConversion(email => email.Value, value => EmailAddress.Create(value).Value)
            .HasMaxLength(256)
            .IsRequired();

        builder.ComplexProperty(payment => payment.Amount, amount =>
        {
            amount.Property(money => money.Amount).HasColumnName("amount").HasPrecision(18, 2);
            amount.Property(money => money.Currency).HasColumnName("currency").HasMaxLength(3);
        });

        builder.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(payment => payment.Reference).HasMaxLength(64);
        builder.Property(payment => payment.DeclineReason).HasMaxLength(400);

        builder.HasIndex(payment => payment.Booking).IsUnique();
    }
}

internal sealed class PaymentRepository(PaymentsDbContext dbContext) : IPaymentRepository
{
    public Task<Payment?> FindAsync(PaymentId id, CancellationToken cancellationToken = default) =>
        dbContext.Payments.FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken);

    public Task<Payment?> FindForBookingAsync(
        BookingReference booking,
        CancellationToken cancellationToken = default) =>
        dbContext.Payments.FirstOrDefaultAsync(payment => payment.Booking == booking, cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListRecentAsync(
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.Payments
            .AsNoTracking()
            .OrderByDescending(payment => payment.DecidedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Payment aggregate, CancellationToken cancellationToken = default) =>
        await dbContext.Payments.AddAsync(aggregate, cancellationToken);
}

internal sealed class PaymentsUnitOfWork(PaymentsDbContext dbContext, IDomainEventDispatcher dispatcher)
    : EfUnitOfWork<PaymentsDbContext>(dbContext, dispatcher);
