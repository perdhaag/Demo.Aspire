using Demo.Aspire.Screenings.Domain;
using Demo.Aspire.Screenings.Features.HoldPolicy;
using Shouldly;

namespace Demo.Aspire.Tests;

public sealed class SeatHoldPolicyTests
{
    [Fact]
    public void The_default_duration_matches_the_screening_aggregates_own_default()
    {
        new SeatHoldPolicy().Duration.ShouldBe(Screening.DefaultHoldDuration);
    }

    [Theory]
    [InlineData(20, 4)]     // a fifth of the duration, comfortably inside the bounds
    [InlineData(3, 2)]      // clamped to the 2s floor: too short a hold to sweep at a fifth
    [InlineData(600, 15)]   // clamped to the 15s ceiling: no need to poll more often than that
    public void The_sweep_interval_scales_with_duration_but_stays_within_bounds(
        int durationSeconds,
        int expectedSweepSeconds)
    {
        var policy = new SeatHoldPolicy();
        policy.Set(TimeSpan.FromSeconds(durationSeconds));

        policy.SweepInterval.ShouldBe(TimeSpan.FromSeconds(expectedSweepSeconds));
    }

    [Theory]
    [InlineData(1, 10)]      // below the 10s floor
    [InlineData(9_999, 600)] // above the 10-minute ceiling
    public void Set_clamps_the_duration_to_a_sane_range(int requestedSeconds, int expectedSeconds)
    {
        var policy = new SeatHoldPolicy();
        policy.Set(TimeSpan.FromSeconds(requestedSeconds));

        policy.Duration.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }
}
