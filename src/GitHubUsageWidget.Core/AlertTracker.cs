namespace GitHubUsageWidget.Core;

public sealed record Alert(WindowKind Kind, TimeSpan Threshold, TimeSpan TimeToLimit, TimeSpan TimeToReset, DateTimeOffset ResetsAt);

/// <summary>Persisted alert state: which thresholds already fired for which window.</summary>
public sealed record AlertState(DateTimeOffset? WindowResetsAt, IReadOnlyList<int> FiredMinutes);

/// <summary>
/// Decides when to pop up a warning. A threshold T fires when the forecast says the limit is reached
/// within T and the reset is at least T + 1h away, i.e. you would sit blocked for an hour or more.
/// Each threshold fires once per window; jumping past several fires only the smallest one.
/// </summary>
public sealed class AlertTracker
{
    /// <summary>Time-to-limit thresholds in minutes, largest first.</summary>
    public static readonly IReadOnlyList<int> ThresholdMinutes = new[] { 120, 60, 30, 15 };

    /// <summary>Minimum predicted blocked time (reset minus limit) for an alert.</summary>
    public static readonly TimeSpan MinBlockedTime = TimeSpan.FromHours(1);

    private DateTimeOffset? _window;
    private readonly HashSet<int> _fired = new();

    public AlertTracker(AlertState? state = null)
    {
        if (state is null)
            return;
        _window = state.WindowResetsAt;
        _fired.UnionWith(state.FiredMinutes);
    }

    public AlertState State => new(_window, _fired.OrderByDescending(m => m).ToArray());

    public Alert? Evaluate(Forecast forecast)
    {
        if (forecast.ResetsAt is not { } resetsAt)
            return null;

        if (_window is not { } known || !Forecaster.SameWindow(known, resetsAt))
        {
            _window = resetsAt;
            _fired.Clear();
        }

        if (forecast.Verdict == Verdict.Exhausted
            || forecast.TimeToLimit is not { } toLimit
            || forecast.TimeToReset is not { } toReset)
            return null;

        int? chosen = null;
        foreach (var minutes in ThresholdMinutes)
        {
            var threshold = TimeSpan.FromMinutes(minutes);
            if (!_fired.Contains(minutes) && toLimit <= threshold && toReset >= threshold + MinBlockedTime)
                chosen = minutes; // keep going: the smallest matching threshold wins
        }

        if (chosen is not { } fire)
            return null;

        foreach (var minutes in ThresholdMinutes.Where(m => m >= fire))
            _fired.Add(minutes);

        return new Alert(forecast.Kind, TimeSpan.FromMinutes(fire), toLimit, toReset, resetsAt);
    }
}
