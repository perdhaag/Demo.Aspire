using Demo.Aspire.Platform.Endpoints;
using Demo.Aspire.Screenings.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Demo.Aspire.Screenings.Features.HoldPolicy;

/// <summary>
/// How long a new seat hold lasts, and how often <see cref="ExpireSeatHolds.SeatHoldSweeper"/>
/// looks for one that has lapsed. A live demo cannot wait out the real three minutes
/// and still hold an audience's attention, so this is a runtime switch rather than
/// configuration read once at startup.
/// </summary>
/// <remarks>
/// A mutable singleton is the right shape here, not <c>IOptionsMonitor</c>: there is no
/// configuration source for a change to this to come from, and the demo needs flipping
/// it to be instant. That is a genuine trade against how the rest of this codebase
/// treats settings — it is a demo control, not a customer-facing one, and should not be
/// mistaken for the pattern to reach for when an actual tenant setting is needed.
/// </remarks>
public sealed class SeatHoldPolicy
{
    private static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);

    public TimeSpan Duration { get; private set; } = Screening.DefaultHoldDuration;

    /// <summary>
    /// How often the sweeper checks for lapsed holds. A hold that is only swept as
    /// often as a three-minute one would not really be short, so this scales down with
    /// the hold &mdash; clamped so a very short hold is still swept more than once
    /// before it matters, and a long one is not polled pointlessly often.
    /// </summary>
    public TimeSpan SweepInterval => TimeSpan.FromSeconds(Math.Clamp(Duration.TotalSeconds / 5, 2, 15));

    public void Set(TimeSpan duration) =>
        Duration = TimeSpan.FromSeconds(
            Math.Clamp(duration.TotalSeconds, MinDuration.TotalSeconds, MaxDuration.TotalSeconds));
}

public sealed record HoldPolicyResponse(int Seconds);

public sealed record SetHoldPolicyRequest(int Seconds);

public sealed class HoldPolicyEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/screenings/hold-policy", (SeatHoldPolicy policy) =>
                TypedResults.Ok(new HoldPolicyResponse((int)policy.Duration.TotalSeconds)))
            .WithName("GetHoldPolicy")
            .WithSummary("How long a newly held seat stays held before it goes back on sale.")
            .WithTags("Screenings");

        routes.MapPost("/screenings/hold-policy", (SetHoldPolicyRequest request, SeatHoldPolicy policy) =>
            {
                policy.Set(TimeSpan.FromSeconds(request.Seconds));
                return TypedResults.Ok(new HoldPolicyResponse((int)policy.Duration.TotalSeconds));
            })
            .WithName("SetHoldPolicy")
            .WithSummary("Changes how long a new seat hold lasts. A demo control, not a customer setting.")
            .WithTags("Screenings");
    }
}
