using Demo.Aspire.Contracts;
using Demo.Aspire.Platform.Domain;
using Demo.Aspire.Platform.Messaging;
using Demo.Aspire.Screenings.Domain;
using Demo.Aspire.Screenings.Features.HoldPolicy;
using Demo.Aspire.SharedKernel;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Demo.Aspire.Screenings.Features.ExpireSeatHolds;

/// <summary>
/// Nobody sends a message when a deadline passes, so something has to look. The sweeper
/// gives the "hold expires" rule a voice: it turns the passage of time into an event the
/// rest of the system can react to.
/// </summary>
internal sealed class SeatHoldSweeper(
    IServiceScopeFactory scopeFactory,
    SeatHoldPolicy holdPolicy,
    TimeProvider clock,
    ILogger<SeatHoldSweeper> logger) : BackgroundService
{
    private const int BatchSize = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(holdPolicy.SweepInterval, clock);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed sweep is not fatal: the next tick tries again, and stale holds
                // are treated as available by the aggregate in the meantime.
                logger.LogError(exception, "Seat hold sweep failed.");
            }

            // Re-read every tick rather than once at start-up, so flipping the demo's
            // "short holds" switch tightens the sweep on its very next pass instead of
            // waiting for this instance to restart.
            if (timer.Period != holdPolicy.SweepInterval)
            {
                timer.Period = holdPolicy.SweepInterval;
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var screenings = scope.ServiceProvider.GetRequiredService<IScreeningRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = clock.GetUtcNow();

        var candidates = await screenings.FindWithLapsedHoldsAsync(now, BatchSize, cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var expired = 0;

        foreach (var screening in candidates)
        {
            expired += screening.ExpireLapsedHolds(now).Count;
        }

        if (expired == 0)
        {
            return;
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Expired {Count} seat hold(s).", expired);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Somebody paid in the same instant. They win; the next sweep will pick up
            // whatever genuinely lapsed.
            logger.LogDebug("Seat hold sweep lost a race with a concurrent booking.");
        }
    }
}

/// <summary>Tells the rest of the system that a customer ran out of time.</summary>
internal sealed class SeatHoldLapsedPublisher(
    IPublishEndpoint publishEndpoint,
    CorrelationContext correlation,
    TimeProvider clock) : IDomainEventHandler<SeatHoldLapsed>
{
    public Task HandleAsync(SeatHoldLapsed domainEvent, CancellationToken cancellationToken) =>
        publishEndpoint.Publish(
            new SeatHoldExpired(
                correlation.Current,
                domainEvent.Booking.Value,
                domainEvent.ScreeningId.Value,
                [.. domainEvent.Seats.Select(seat => seat.ToString())],
                clock.GetUtcNow()),
            cancellationToken);
}
