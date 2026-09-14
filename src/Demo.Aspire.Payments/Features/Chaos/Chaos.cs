using System.Text.Json.Serialization;
using Demo.Aspire.Platform.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Demo.Aspire.Payments.Features.Chaos;

/// <summary>What the simulated card network is doing to every authorization right now.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChaosMode>))]
public enum ChaosMode
{
    None,
    Paused,
    Slow,
    Failing,
}

/// <summary>
/// A demo-only fault injector for the one dependency Payments has: the simulated card
/// network. Reliability is the least visible thing this codebase does &mdash; the
/// outbox, the inbox, MassTransit's retry policy &mdash; and this switch exists so an
/// audience can watch those mechanisms actually do something instead of taking the
/// README's word for it.
/// </summary>
public sealed class ChaosSwitch(TimeProvider clock)
{
    private readonly Lock gate = new();

    private TaskCompletionSource? pauseGate;

    private int remainingFailures;

    public ChaosMode Mode { get; private set; } = ChaosMode.None;

    public bool IsSlow => Mode == ChaosMode.Slow;

    /// <param name="failures">
    /// For <see cref="ChaosMode.Failing"/> only: how many authorizations in a row throw
    /// before the switch returns itself to <see cref="ChaosMode.None"/> on its own.
    /// </param>
    public void Set(ChaosMode mode, int failures = 2)
    {
        TaskCompletionSource? toRelease = null;

        lock (gate)
        {
            Mode = mode;
            remainingFailures = mode == ChaosMode.Failing ? Math.Max(1, failures) : 0;

            if (mode != ChaosMode.Paused)
            {
                toRelease = pauseGate;
                pauseGate = null;
            }
        }

        toRelease?.TrySetResult();
    }

    /// <summary>
    /// Claims one simulated failure, or reports there is none left to claim. Burns the
    /// count down to zero and flips the mode back to <see cref="ChaosMode.None"/> on the
    /// last one, so "fail twice, then succeed" is something that happens once rather
    /// than a mode someone has to remember to turn off.
    /// </summary>
    public bool TryConsumeFailure()
    {
        lock (gate)
        {
            if (Mode != ChaosMode.Failing || remainingFailures <= 0)
            {
                return false;
            }

            remainingFailures--;

            if (remainingFailures == 0)
            {
                Mode = ChaosMode.None;
            }

            return true;
        }
    }

    /// <summary>
    /// Blocks the caller while paused, exactly as a real card network hanging would
    /// leave the message unacknowledged on the queue &mdash; not retried, not
    /// dead-lettered, just waiting. Bounded to five minutes so a forgotten pause cannot
    /// wedge the demo permanently.
    /// </summary>
    public Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource? toAwait;

        lock (gate)
        {
            if (Mode != ChaosMode.Paused)
            {
                return Task.CompletedTask;
            }

            toAwait = pauseGate ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return AwaitWithCeilingAsync(toAwait, cancellationToken);
    }

    private async Task AwaitWithCeilingAsync(TaskCompletionSource gateToAwait, CancellationToken cancellationToken)
    {
        using var ceiling = new CancellationTokenSource(TimeSpan.FromMinutes(5), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ceiling.Token);

        await using (linked.Token.Register(() => gateToAwait.TrySetCanceled(linked.Token)).ConfigureAwait(false))
        {
            await gateToAwait.Task.ConfigureAwait(false);
        }
    }
}

public sealed record ChaosResponse(ChaosMode Mode);

public sealed record SetChaosRequest(ChaosMode Mode);

public sealed class ChaosEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/payments/chaos", (ChaosSwitch chaos) => TypedResults.Ok(new ChaosResponse(chaos.Mode)))
            .WithName("GetChaosMode")
            .WithSummary("What the simulated card network is doing right now. A demo control, not a customer setting.")
            .WithTags("Payments");

        routes.MapPost("/payments/chaos", (SetChaosRequest request, ChaosSwitch chaos) =>
            {
                chaos.Set(request.Mode);
                return TypedResults.Ok(new ChaosResponse(chaos.Mode));
            })
            .WithName("SetChaosMode")
            .WithSummary("Pauses, slows, or breaks the simulated card network. A demo control, not a customer setting.")
            .WithTags("Payments");
    }
}
