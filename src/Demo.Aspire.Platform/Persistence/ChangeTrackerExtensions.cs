using Demo.Aspire.SharedKernel;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Demo.Aspire.Platform.Persistence;

public static class ChangeTrackerExtensions
{
    extension(ChangeTracker changeTracker)
    {
        /// <summary>Removes and returns every domain event queued on tracked aggregates.</summary>
        public IReadOnlyList<IDomainEvent> DrainDomainEvents()
        {
            IHasDomainEvents[] aggregates =
            [
                .. changeTracker.Entries<IHasDomainEvents>()
                    .Select(entry => entry.Entity)
                    .Where(aggregate => aggregate.DomainEvents.Count > 0),
            ];

            return aggregates.Length == 0
                ? []
                : [.. aggregates.SelectMany(aggregate => aggregate.DrainDomainEvents())];
        }
    }
}
