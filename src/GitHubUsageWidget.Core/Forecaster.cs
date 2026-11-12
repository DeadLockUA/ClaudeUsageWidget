namespace GitHubUsageWidget.Core;

/// <summary>One stored poll result for a window.</summary>
public sealed record UsageSample(DateTimeOffset At, double Utilization, DateTimeOffset ResetsAt);

public enum RateSource
{
    None,
    Average,
    Recent,
}

public enum Verdict
{
    /// <summary>Window has not started (no reset time).</summary>
    Idle,
    /// <summary>At the current pace the limit is reached comfortably after the reset.</summary>
    Ok,
    /// <summary>At the current pace the limit is reached shortly after the reset.</summary>
    Tight,
    /// <summary>At the current pace the limit is reached before the reset.</summary>
    RunsOut,
    /// <summary>The limit is already reached.</summary>
    Exhausted,
}

public sealed record Forecast(
    WindowKind Kind,
    double RemainingPercent,
    DateTimeOffset? ResetsAt,
    TimeSpan? TimeToReset,
    double? RatePerHour,
    RateSource RateSource,
    TimeSpan? TimeToLimit,
    DateTimeOffset? LimitAt,
    Verdict Verdict);

public static class Forecaster
{
    /// <summary>How far back the "current pace" looks.</summary>
    public static readonly TimeSpan RecentSpan = TimeSpan.FromHours(1);

    /// <summary>Minimum history span for the current pace; shorter spans are too noisy with integer percents.</summary>
    public static readonly TimeSpan MinRecentSpan = TimeSpan.FromMinutes(20);

    /// <summary>Minimum elapsed window time before the average pace is meaningful.</summary>
    public static readonly TimeSpan MinAverageSpan = TimeSpan.FromMinutes(5);

    /// <summary>Limit reached within this factor of the time to reset counts as tight.</summary>
    public const double TightFactor = 1.25;

    /// <summary>Two reset times closer than this belong to the same window (the server jitters sub-second).</summary>
    public static readonly TimeSpan SameWindowTolerance = TimeSpan.FromMinutes(1);

    public static Forecast Forecast(WindowKind kind, LimitWindow window, DateTimeOffset now, IReadOnlyList<UsageSample> history)
    {
        var used = Math.Clamp(window.Utilization, 0, 100);
        var remaining = 100 - used;

        if (window.ResetsAt is not { } resetsAt)
            return new Forecast(kind, 100, null, null, null, RateSource.None, null, null, Verdict.Idle);

        var timeToReset = resetsAt > now ? resetsAt - now : TimeSpan.Zero;

        if (used >= 100)
            return new Forecast(kind, 0, resetsAt, timeToReset, null, RateSource.None, TimeSpan.Zero, now, Verdict.Exhausted);

        var (rate, source) = RecentRate(window, resetsAt, now, history) is { } recent
            ? (recent, RateSource.Recent)
            : AverageRate(kind, used, resetsAt, now) is { } avg
                ? (avg, RateSource.Average)
                : ((double?)null, RateSource.None);

        if (rate is not > 0)
            return new Forecast(kind, remaining, resetsAt, timeToReset, rate, source, null, null, Verdict.Ok);

        var timeToLimit = TimeSpan.FromHours(remaining / rate.Value);
        var verdict = timeToLimit < timeToReset
            ? Verdict.RunsOut
            : timeToLimit < timeToReset * TightFactor ? Verdict.Tight : Verdict.Ok;

        return new Forecast(kind, remaining, resetsAt, timeToReset, rate, source, timeToLimit, now + timeToLimit, verdict);
    }

    public static bool SameWindow(DateTimeOffset a, DateTimeOffset b) => (a - b).Duration() < SameWindowTolerance;

    private static double? RecentRate(LimitWindow window, DateTimeOffset resetsAt, DateTimeOffset now, IReadOnlyList<UsageSample> history)
    {
        var from = now - RecentSpan;
        UsageSample? oldest = null;
        foreach (var s in history)
        {
            if (s.At < from || s.At >= now || !SameWindow(s.ResetsAt, resetsAt))
                continue;
            if (oldest is null || s.At < oldest.At)
                oldest = s;
        }

        if (oldest is null || now - oldest.At < MinRecentSpan)
            return null;

        return Math.Max(0, window.Utilization - oldest.Utilization) / (now - oldest.At).TotalHours;
    }

    private static double? AverageRate(WindowKind kind, double used, DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var elapsed = now - (resetsAt - kind.Length());
        if (elapsed < MinAverageSpan)
            return null;
        return used / elapsed.TotalHours;
    }
}
