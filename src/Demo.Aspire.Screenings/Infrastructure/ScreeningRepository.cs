using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Persistence;
using Demo.Aspire.Screenings.Domain;
using Microsoft.EntityFrameworkCore;

namespace Demo.Aspire.Screenings.Infrastructure;

internal sealed class ScreeningRepository(ScreeningsDbContext dbContext) : IScreeningRepository
{
    public Task<Screening?> FindAsync(ScreeningId id, CancellationToken cancellationToken = default) =>
        dbContext.Screenings.FirstOrDefaultAsync(screening => screening.Id == id, cancellationToken);

    public Task<Screening?> FindWithSeatsAsync(ScreeningId id, CancellationToken cancellationToken = default) =>
        dbContext.Screenings
            .Include(screening => screening.Seats)
            .FirstOrDefaultAsync(screening => screening.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Screening>> FindWithLapsedHoldsAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.Screenings
            .Include(screening => screening.Seats)
            .Where(screening => screening.Seats.Any(seat =>
                seat.Status == SeatStatus.Held && seat.HoldExpiresAtUtc <= now))
            .OrderBy(screening => screening.StartsAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Screening aggregate, CancellationToken cancellationToken = default) =>
        await dbContext.Screenings.AddAsync(aggregate, cancellationToken);
}

internal sealed class ScreeningsUnitOfWork(ScreeningsDbContext dbContext, IDomainEventDispatcher dispatcher)
    : EfUnitOfWork<ScreeningsDbContext>(dbContext, dispatcher);
