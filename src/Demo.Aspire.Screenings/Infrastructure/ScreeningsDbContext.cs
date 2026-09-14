using Demo.Aspire.Screenings.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Demo.Aspire.Screenings.Infrastructure;

public sealed class ScreeningsDbContext(DbContextOptions<ScreeningsDbContext> options) : DbContext(options)
{
    public DbSet<Screening> Screenings => Set<Screening>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("screenings");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScreeningsDbContext).Assembly);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
