using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Platform.Persistence;

/// <summary>Puts the starting state of a bounded context in place on first run.</summary>
public interface IDatabaseSeeder<in TDbContext>
    where TDbContext : DbContext
{
    Task SeedAsync(TDbContext dbContext, CancellationToken cancellationToken);
}

/// <summary>
/// Creates the schema and runs the seeders once at start-up. Aspire's <c>WaitFor</c>
/// already guarantees the server is healthy, so this only has to deal with the schema.
/// </summary>
/// <remarks>
/// This is a plain <see cref="IHostedService"/> rather than a <c>BackgroundService</c>
/// on purpose: <see cref="StartAsync"/> is awaited before the next hosted service starts,
/// so registering it ahead of MassTransit guarantees the outbox tables exist before the
/// delivery service goes looking for them.
/// </remarks>
public sealed class DatabaseInitializer<TDbContext>(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseInitializer<TDbContext>> logger) : IHostedService
    where TDbContext : DbContext
{
    public static readonly ActivitySource ActivitySource = new($"Demo.Aspire.Initialize.{typeof(TDbContext).Name}");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity($"Initialise {typeof(TDbContext).Name}");

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        foreach (var seeder in scope.ServiceProvider.GetServices<IDatabaseSeeder<TDbContext>>())
        {
            await seeder.SeedAsync(dbContext, cancellationToken);
        }

        logger.LogInformation("{DbContext} is ready.", typeof(TDbContext).Name);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
