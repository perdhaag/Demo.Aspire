namespace Demo.Aspire.SharedKernel;

/// <summary>
/// Collection-like access to one aggregate type. Repositories are declared by the
/// domain and implemented by infrastructure, so slices never see a DbContext.
/// </summary>
public interface IRepository<TAggregate, in TId>
    where TAggregate : AggregateRoot<TId>
    where TId : struct, IEquatable<TId>
{
    Task<TAggregate?> FindAsync(TId id, CancellationToken cancellationToken = default);

    Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);
}

/// <summary>Commits one atomic change to a single aggregate (plus its outbox rows).</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
