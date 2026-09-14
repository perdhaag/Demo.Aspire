using Demo.Aspire.Platform.Domain;
using Demo.Aspire.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Demo.Aspire.Platform.Persistence;

/// <summary>
/// Saves one atomic change. Domain events are dispatched <em>before</em> the write, so
/// anything a handler adds &mdash; most importantly MassTransit's outbox rows &mdash;
/// lands in the same transaction as the aggregate that caused it.
/// </summary>
/// <remarks>
/// An EF <c>SaveChangesInterceptor</c> could do this implicitly. An explicit unit of
/// work is used here instead because the ordering guarantee is the whole point, and
/// hiding it behind an interceptor makes that guarantee easy to break by accident.
/// </remarks>
public class EfUnitOfWork<TDbContext>(TDbContext dbContext, IDomainEventDispatcher dispatcher) : IUnitOfWork
    where TDbContext : DbContext
{
    private const int MaxDispatchPasses = 8;

    protected TDbContext DbContext { get; } = dbContext;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        for (var pass = 0; ; pass++)
        {
            var pending = DbContext.ChangeTracker.DrainDomainEvents();

            if (pending.Count == 0)
            {
                break;
            }

            if (pass == MaxDispatchPasses)
            {
                throw new InvalidOperationException(
                    $"Domain events were still being raised after {MaxDispatchPasses} dispatch passes; "
                    + "this looks like a cycle.");
            }

            await dispatcher.DispatchAsync(pending, cancellationToken);
        }

        return await DbContext.SaveChangesAsync(cancellationToken);
    }
}
