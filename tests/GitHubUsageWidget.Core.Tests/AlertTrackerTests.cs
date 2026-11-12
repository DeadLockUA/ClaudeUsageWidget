using GitHubUsageWidget.Core;

namespace GitHubUsageWidget.Core.Tests;

public class AlertTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    private static Forecast F(TimeSpan? toLimit, TimeSpan toReset, DateTimeOffset? resetsAt = null)
    {
        var reset = resetsAt ?? Now + toReset;
        var verdict = toLimit is null ? Verdict.Ok : toLimit < toReset ? Verdict.RunsOut : Verdict.Ok;
        return new Forecast(WindowKind.FiveHour, 50, reset, toReset, 10, RateSource.Recent,
            toLimit, toLimit is null ? null : Now + toLimit, verdict);
    }

    private static TimeSpan H(double h) => TimeSpan.FromHours(h);
    private static TimeSpan M(double m) => TimeSpan.FromMinutes(m);

    [Fact]
    public void Two_hours_to_limit_and_three_to_reset_fires_two_hour_alert()
    {
        var alert = new AlertTracker().Evaluate(F(H(2), H(3)));

        Assert.NotNull(alert);
        Assert.Equal(H(2), alert!.Threshold);
    }

    [Fact]
    public void Two_hours_to_limit_but_less_than_three_to_reset_does_not_fire()
    {
        Assert.Null(new AlertTracker().Evaluate(F(H(2), H(2.9))));
    }

    [Theory]
    [InlineData(60, 120, 60)]
    [InlineData(30, 90, 30)]
    [InlineData(15, 75, 15)]
    public void Each_user_threshold_fires_at_its_boundary(double toLimitMin, double toResetMin, double expectedThresholdMin)
    {
        var alert = new AlertTracker().Evaluate(F(M(toLimitMin), M(toResetMin)));

        Assert.Equal(M(expectedThresholdMin), alert!.Threshold);
    }

    [Fact]
    public void More_than_two_hours_to_limit_does_not_fire()
    {
        Assert.Null(new AlertTracker().Evaluate(F(H(2.1), H(4))));
    }

    [Fact]
    public void Same_threshold_fires_only_once_per_window()
    {
        var tracker = new AlertTracker();
        var reset = Now + H(3);

        Assert.NotNull(tracker.Evaluate(F(H(2), H(3), reset)));
        Assert.Null(tracker.Evaluate(F(H(1.9), H(2.95), reset)));
    }

    [Fact]
    public void Slowing_down_and_speeding_up_does_not_refire()
    {
        var tracker = new AlertTracker();
        var reset = Now + H(4);

        tracker.Evaluate(F(H(2), H(4), reset));
        tracker.Evaluate(F(H(5), H(4), reset));

        Assert.Null(tracker.Evaluate(F(H(1.5), H(3.5), reset)));
    }

    [Fact]
    public void Next_threshold_fires_after_previous_one()
    {
        var tracker = new AlertTracker();
        var reset = Now + H(4);

        tracker.Evaluate(F(H(2), H(4), reset));
        var alert = tracker.Evaluate(F(M(55), H(3), reset));

        Assert.Equal(H(1), alert!.Threshold);
    }

    [Fact]
    public void Jumping_past_several_thresholds_fires_only_the_smallest_once()
    {
        var tracker = new AlertTracker();
        var reset = Now + H(4);

        var alert = tracker.Evaluate(F(M(20), H(4), reset));

        Assert.Equal(M(30), alert!.Threshold);
        Assert.Null(tracker.Evaluate(F(M(19), H(3.9), reset)));
    }

    [Fact]
    public void New_window_rearms_thresholds()
    {
        var tracker = new AlertTracker();

        tracker.Evaluate(F(H(2), H(3), Now + H(3)));
        var alert = tracker.Evaluate(F(H(2), H(3.5), Now + H(8)));

        Assert.Equal(H(2), alert!.Threshold);
    }

    [Fact]
    public void No_limit_forecast_does_not_fire()
    {
        Assert.Null(new AlertTracker().Evaluate(F(null, H(3))));
    }

    [Fact]
    public void State_round_trips_so_restart_does_not_refire()
    {
        var reset = Now + H(3);
        var first = new AlertTracker();
        first.Evaluate(F(H(2), H(3), reset));

        var restored = new AlertTracker(first.State);

        Assert.Null(restored.Evaluate(F(H(2), H(3), reset)));
    }

    [Fact]
    public void Exhausted_window_does_not_fire()
    {
        var exhausted = new Forecast(WindowKind.FiveHour, 0, Now + H(3), H(3), null, RateSource.None,
            TimeSpan.Zero, Now, Verdict.Exhausted);

        Assert.Null(new AlertTracker().Evaluate(exhausted));
    }
}
