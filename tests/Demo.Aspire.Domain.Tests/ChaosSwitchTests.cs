using Demo.Aspire.Payments.Features.Chaos;
using Shouldly;

namespace Demo.Aspire.Tests;

/// <summary>
/// The demo's fault injector for Payments, tested the same way the aggregates are: no
/// database, no bus, just the object and the clock it was given.
/// </summary>
public sealed class ChaosSwitchTests
{
    private static ChaosSwitch NewSwitch() => new(TimeProvider.System);

    [Fact]
    public void A_fresh_switch_injects_nothing()
    {
        var chaos = NewSwitch();

        chaos.Mode.ShouldBe(ChaosMode.None);
        chaos.IsSlow.ShouldBeFalse();
        chaos.TryConsumeFailure().ShouldBeFalse();
    }

    [Fact]
    public void Failing_burns_down_to_none_after_exactly_the_requested_count()
    {
        var chaos = NewSwitch();
        chaos.Set(ChaosMode.Failing, failures: 2);

        chaos.TryConsumeFailure().ShouldBeTrue();
        chaos.Mode.ShouldBe(ChaosMode.Failing); // one failure still owed

        chaos.TryConsumeFailure().ShouldBeTrue();
        chaos.Mode.ShouldBe(ChaosMode.None); // the last one also turns the switch off

        chaos.TryConsumeFailure().ShouldBeFalse();
    }

    [Fact]
    public void Slow_mode_never_consumes_a_failure()
    {
        var chaos = NewSwitch();
        chaos.Set(ChaosMode.Slow);

        chaos.IsSlow.ShouldBeTrue();
        chaos.TryConsumeFailure().ShouldBeFalse();
        chaos.Mode.ShouldBe(ChaosMode.Slow);
    }

    [Fact]
    public async Task Waiting_while_paused_completes_as_soon_as_the_switch_is_cleared()
    {
        var chaos = NewSwitch();
        chaos.Set(ChaosMode.Paused);

        var waiting = chaos.WaitWhilePausedAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        waiting.IsCompleted.ShouldBeFalse();

        chaos.Set(ChaosMode.None);

        await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Waiting_when_not_paused_returns_immediately()
    {
        var chaos = NewSwitch();

        await chaos.WaitWhilePausedAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }
}
