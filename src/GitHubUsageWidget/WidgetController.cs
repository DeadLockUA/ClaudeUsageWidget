using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using GitHubUsageWidget.Core;

namespace GitHubUsageWidget;

/// <summary>What the UI renders.</summary>
internal sealed record WidgetState(
    Forecast? FiveHour,
    Forecast? Weekly,
    DateTimeOffset? UpdatedAt,
    string? Problem)
{
    /// <summary>Data exists but the last poll did not refresh it.</summary>
    public bool IsStale => Problem is not null;

    public static readonly WidgetState Empty = new(null, null, null, "Loading...");
}

/// <summary>Polls the usage endpoint, keeps history, computes forecasts and raises alerts.</summary>
internal sealed class WidgetController : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinBackoff = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan HistoryKeep = TimeSpan.FromDays(8);

    private readonly UsageClient _client = new(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
    private readonly HistoryStore _history = new(AppStorage.HistoryPath);
    private readonly AlertTracker _alerts = new(AppStorage.LoadAlertState());
    private readonly DispatcherTimer _timer = new();
    private UsageSnapshot? _last;
    private bool _busy;

    public WidgetState State { get; private set; } = WidgetState.Empty;

    public event Action<WidgetState>? StateChanged;
    public event Action<Alert>? AlertRaised;

    public WidgetController()
    {
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public void Start()
    {
        _history.Prune(DateTimeOffset.UtcNow - HistoryKeep);
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_busy)
            return;
        _busy = true;
        _timer.Stop();
        var next = PollInterval;
        try
        {
            next = await PollAsync();
        }
        catch (Exception ex)
        {
            AppStorage.Log("Poll crashed: " + ex);
            Publish("Error: " + ex.Message);
        }
        finally
        {
            _busy = false;
            _timer.Interval = next;
            _timer.Start();
        }
    }

    private async Task<TimeSpan> PollAsync()
    {
        var now = DateTimeOffset.UtcNow;

        Credentials creds;
        try
        {
            creds = CredentialsReader.Parse(await File.ReadAllTextAsync(CredentialsReader.DefaultPath()));
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            Publish("Claude Code login not found");
            AppStorage.Log("Credentials: " + ex.Message);
            return PollInterval;
        }

        if (creds.ExpiresAt is { } exp && exp <= now)
        {
            Publish("Login expired - open Claude Code to renew it");
            return PollInterval;
        }

        var result = await _client.FetchAsync(creds.AccessToken, now, CancellationToken.None);
        switch (result.Status)
        {
            case FetchStatus.Ok:
                Accept(result.Snapshot!);
                return PollInterval;
            case FetchStatus.Unauthorized:
                Publish("Login rejected - open Claude Code to renew it");
                return PollInterval;
            case FetchStatus.RateLimited:
                var wait = result.RetryAfter is { } ra && ra > MinBackoff ? ra : MinBackoff;
                Publish($"Rate limited, retry in {DisplayFormatter.Duration(wait)}");
                return wait;
            default:
                AppStorage.Log("Fetch failed: " + result.Error);
                Publish("Cannot reach server (" + result.Error + ")");
                return PollInterval;
        }
    }

    private void Accept(UsageSnapshot snapshot)
    {
        _last = snapshot;
        Record(WindowKind.FiveHour, snapshot.FiveHour, snapshot.FetchedAt);
        Record(WindowKind.Weekly, snapshot.Weekly, snapshot.FetchedAt);
        Publish(problem: null);

        // Only the 5-hour window pops up alerts; the weekly one is shown on the strip only.
        var alert = State.FiveHour is { } five ? _alerts.Evaluate(five) : null;
        AppStorage.SaveAlertState(_alerts.State);
        if (alert is not null)
            AlertRaised?.Invoke(alert);
    }

    private void Record(WindowKind kind, LimitWindow? window, DateTimeOffset at)
    {
        if (window?.ResetsAt is { } reset)
            _history.Add(kind, new UsageSample(at, window.Utilization, reset));
    }

    private void Publish(string? problem)
    {
        var s = _last;
        State = new WidgetState(
            s?.FiveHour is { } f ? Forecaster.Forecast(WindowKind.FiveHour, f, s.FetchedAt, _history.Samples(WindowKind.FiveHour)) : null,
            s?.Weekly is { } w ? Forecaster.Forecast(WindowKind.Weekly, w, s.FetchedAt, _history.Samples(WindowKind.Weekly)) : null,
            s?.FetchedAt,
            problem);
        StateChanged?.Invoke(State);
    }

    public void Dispose() => _timer.Stop();
}
