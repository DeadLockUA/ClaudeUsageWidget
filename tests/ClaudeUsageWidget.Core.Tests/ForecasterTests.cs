using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.Core.Tests;

public class ForecasterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<UsageSample> NoHistory = Array.Empty<UsageSample>();

    // 5h window that started 2h ago and resets in 3h.
    private static readonly DateTimeOffset FiveHourReset = Now.AddHours(3);

    [Fact]
    public void Remaining_is_hundred_minus_utilization()
    {
        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(30, FiveHourReset), Now, NoHistory);

        Assert.Equal(70, f.RemainingPercent);
        Assert.Equal(TimeSpan.FromHours(3), f.TimeToReset);
    }

    [Fact]
    public void Without_history_uses_average_since_window_start()
    {
        // 40% used in 2h => 20%/h; 60% left => 3h to limit == time to reset.
        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(40, FiveHourReset), Now, NoHistory);

        Assert.Equal(RateSource.Average, f.RateSource);
        Assert.Equal(20, f.RatePerHour!.Value, 6);
        Assert.Equal(TimeSpan.FromHours(3), f.TimeToLimit);
        Assert.Equal(Now.AddHours(3), f.LimitAt);
    }

    [Fact]
    public void With_enough_recent_history_uses_last_hour_rate()
    {
        var history = new[]
        {
            new UsageSample(Now.AddMinutes(-90), 10, FiveHourReset), // older than an hour: ignored
            new UsageSample(Now.AddMinutes(-60), 20, FiveHourReset),
            new UsageSample(Now.AddMinutes(-30), 30, FiveHourReset),
        };

        // 20% -> 50% over the last hour => 30%/h; 50% left => 1h40m.
        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(50, FiveHourReset), Now, history);

        Assert.Equal(RateSource.Recent, f.RateSource);
        Assert.Equal(30, f.RatePerHour!.Value, 6);
        Assert.Equal(TimeSpan.FromMinutes(100), f.TimeToLimit);
        Assert.Equal(Verdict.RunsOut, f.Verdict);
    }

    [Fact]
    public void Recent_history_shorter_than_twenty_minutes_falls_back_to_average()
    {
        var history = new[] { new UsageSample(Now.AddMinutes(-10), 35, FiveHourReset) };

        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(40, FiveHourReset), Now, history);

        Assert.Equal(RateSource.Average, f.RateSource);
    }

    [Fact]
    public void History_from_a_previous_window_is_ignored()
    {
        var history = new[] { new UsageSample(Now.AddMinutes(-50), 90, Now.AddMinutes(-40)) };

        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(40, FiveHourReset), Now, history);

        Assert.Equal(RateSource.Average, f.RateSource);
    }

    [Fact]
    public void Reset_time_jitter_within_a_minute_is_same_window()
    {
        var history = new[] { new UsageSample(Now.AddMinutes(-60), 20, FiveHourReset.AddSeconds(-0.5)) };

        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(50, FiveHourReset), Now, history);

        Assert.Equal(RateSource.Recent, f.RateSource);
    }

    [Fact]
    public void No_recent_burn_means_ok_and_no_limit_time()
    {
        var history = new[] { new UsageSample(Now.AddMinutes(-60), 50, FiveHourReset) };

        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(50, FiveHourReset), Now, history);

        Assert.Equal(0, f.RatePerHour);
        Assert.Null(f.TimeToLimit);
        Assert.Null(f.LimitAt);
        Assert.Equal(Verdict.Ok, f.Verdict);
    }

    [Fact]
    public void Limit_well_after_reset_is_ok()
    {
        // 10% in 2h => 5%/h; 90% left => 18h, reset in 3h.
        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(10, FiveHourReset), Now, NoHistory);

        Assert.Equal(Verdict.Ok, f.Verdict);
    }

    [Fact]
    public void Limit_just_after_reset_is_tight()
    {
        // 36% in 2h => 18%/h; 64% left => ~3h33m, reset in 3h (within 25% margin).
        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(36, FiveHourReset), Now, NoHistory);

        Assert.Equal(Verdict.Tight, f.Verdict);
    }

    [Fact]
    public void Limit_before_reset_runs_out()
    {
        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(60, FiveHourReset), Now, NoHistory);

        Assert.Equal(Verdict.RunsOut, f.Verdict);
    }

    [Fact]
    public void Full_utilization_is_exhausted()
    {
        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(100, FiveHourReset), Now, NoHistory);

        Assert.Equal(Verdict.Exhausted, f.Verdict);
        Assert.Equal(0, f.RemainingPercent);
        Assert.Equal(TimeSpan.Zero, f.TimeToLimit);
    }

    [Fact]
    public void Window_without_reset_time_is_idle()
    {
        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(0, null), Now, NoHistory);

        Assert.Equal(Verdict.Idle, f.Verdict);
        Assert.Equal(100, f.RemainingPercent);
        Assert.Null(f.TimeToReset);
    }

    [Fact]
    public void Weekly_average_uses_seven_day_window_length()
    {
        // Weekly window resets in 3.5 days => started 3.5 days ago; 70% used => 20%/day.
        var reset = Now.AddDays(3.5);

        var f = Forecaster.Forecast(WindowKind.Weekly, new LimitWindow(70, reset), Now, NoHistory);

        Assert.Equal(20.0 / 24, f.RatePerHour!.Value, 6);
        Assert.Equal(TimeSpan.FromDays(1.5), f.TimeToLimit);
        Assert.Equal(Verdict.RunsOut, f.Verdict);
    }

    [Fact]
    public void Window_just_started_has_no_rate()
    {
        // Started 2 minutes ago: too early to estimate.
        var f = Forecaster.Forecast(WindowKind.FiveHour, new LimitWindow(1, Now.AddHours(5).AddMinutes(-2)), Now, NoHistory);

        Assert.Equal(RateSource.None, f.RateSource);
        Assert.Null(f.TimeToLimit);
        Assert.Equal(Verdict.Ok, f.Verdict);
    }
}
