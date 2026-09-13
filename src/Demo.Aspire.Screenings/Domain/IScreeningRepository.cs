using Demo.Aspire.SharedKernel;

namespace Demo.Aspire.Screenings.Domain;

public interface IScreeningRepository : IRepository<Screening, ScreeningId>
{
    /// <summary>Loads a screening together with its seat map, ready to be changed.</summary>
    Task<Screening?> FindWithSeatsAsync(ScreeningId id, CancellationToken cancellationToken = default);

    /// <summary>Screenings that still have holds which may have run out.</summary>
    Task<IReadOnlyList<Screening>> FindWithLapsedHoldsAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken = default);
}
