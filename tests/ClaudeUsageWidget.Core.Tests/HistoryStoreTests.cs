using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.Core.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cuw-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "history.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_means_empty_history()
    {
        var store = new HistoryStore(FilePath);

        Assert.Empty(store.Samples(WindowKind.FiveHour));
    }

    [Fact]
    public void Added_samples_survive_reload_per_window_kind()
    {
        var store = new HistoryStore(FilePath);
        store.Add(WindowKind.FiveHour, new UsageSample(Now, 11, Now.AddHours(3)));
        store.Add(WindowKind.Weekly, new UsageSample(Now, 79, Now.AddDays(2)));

        var reloaded = new HistoryStore(FilePath);

        var five = Assert.Single(reloaded.Samples(WindowKind.FiveHour));
        Assert.Equal(new UsageSample(Now, 11, Now.AddHours(3)), five);
        Assert.Equal(79, Assert.Single(reloaded.Samples(WindowKind.Weekly)).Utilization);
    }

    [Fact]
    public void Corrupt_lines_are_skipped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "garbage\n");
        var store = new HistoryStore(FilePath);
        store.Add(WindowKind.FiveHour, new UsageSample(Now, 5, Now.AddHours(1)));

        Assert.Single(new HistoryStore(FilePath).Samples(WindowKind.FiveHour));
    }

    [Fact]
    public void Prune_drops_old_samples_from_memory_and_disk()
    {
        var store = new HistoryStore(FilePath);
        store.Add(WindowKind.Weekly, new UsageSample(Now.AddDays(-9), 1, Now.AddDays(-2)));
        store.Add(WindowKind.Weekly, new UsageSample(Now, 2, Now.AddDays(5)));

        store.Prune(olderThan: Now.AddDays(-8));

        Assert.Single(store.Samples(WindowKind.Weekly));
        Assert.Single(new HistoryStore(FilePath).Samples(WindowKind.Weekly));
    }
}
