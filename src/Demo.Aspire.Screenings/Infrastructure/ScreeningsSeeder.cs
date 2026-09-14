using Demo.Aspire.Platform.Persistence;
using Demo.Aspire.Screenings.Domain;
using Demo.Aspire.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Screenings.Infrastructure;

/// <summary>Gives the demo something to book the moment the app host comes up.</summary>
internal sealed class ScreeningsSeeder(TimeProvider clock, ILogger<ScreeningsSeeder> logger)
    : IDatabaseSeeder<ScreeningsDbContext>
{
    private static readonly (string Film, string Auditorium, int InHours, decimal Price, int Rows, int PerRow)[] Catalogue =
    [
        ("Dune: Part Three", "Sal 1 – Store Sal", 3, 199m, 8, 12),
        ("The Seventh Seal (1957)", "Sal 2 – Cinemateket", 6, 149m, 5, 8),
        ("Kong Winter", "Sal 3", 26, 179m, 6, 10),
        ("Trollhunter: Remastered", "Sal 1 – Store Sal", 30, 159m, 8, 12),
    ];

    public async Task SeedAsync(ScreeningsDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await dbContext.Screenings.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();

        foreach (var (film, auditorium, inHours, price, rows, perRow) in Catalogue)
        {
            var screening = Screening.Schedule(
                film,
                auditorium,
                now.AddHours(inHours),
                Money.From(price),
                rows,
                perRow,
                now);

            if (screening.IsFailure)
            {
                logger.LogWarning("Skipping seed screening '{Film}': {Error}", film, screening.Error);
                continue;
            }

            screening.Value.DrainDomainEvents();
            dbContext.Screenings.Add(screening.Value);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} screenings.", Catalogue.Length);
    }
}
