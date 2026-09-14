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
                logger.LogError(exception, "Seat hold sweep failed.");
            }

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
    public Task HandleAsync(SeatHoldLapsed domainEvent, CancellationToken cancellationToken)
    {
        // Everywhere else in the system the correlation arrives on the message being reacted
        // to. Here there is no message: the sweeper is woken by a clock, so nothing has
        // stamped the context and CorrelationContext would mint a fresh id. The two rows
        // that matter most on the bus tape — the hold lapsing, and Bookings cancelling
        // because of it — would then be filed under an id this booking shares with nothing,
        // and the one flow in the demo that is driven by time would be the one flow you
        // could not follow. The booking the hold belonged to is the correlation, as always.
        correlation.Current = domainEvent.Booking.Value;

        return publishEndpoint.Publish(
            new SeatHoldExpired(
                correlation.Current,
                domainEvent.Booking.Value,
                domainEvent.ScreeningId.Value,
                [.. domainEvent.Seats.Select(seat => seat.ToString())],
                clock.GetUtcNow()),
            cancellationToken);
    }
}
