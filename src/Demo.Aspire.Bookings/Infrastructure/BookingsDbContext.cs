using Demo.Aspire.Bookings.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Demo.Aspire.Bookings.Infrastructure;

public sealed class BookingsDbContext(DbContextOptions<BookingsDbContext> options) : DbContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("bookings");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingsDbContext).Assembly);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
