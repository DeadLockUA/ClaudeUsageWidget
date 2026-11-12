using System.Text.Json;

namespace GitHubUsageWidget.Core;

/// <summary>Append-only JSON-lines log of poll results, used for the "current pace" estimate.</summary>
public sealed class HistoryStore
{
    private sealed record Line(WindowKind K, DateTimeOffset T, double U, DateTimeOffset R);

    private readonly string _path;
    private readonly List<(WindowKind Kind, UsageSample Sample)> _items = new();

    public HistoryStore(string path)
    {
        _path = path;
        if (!File.Exists(path))
            return;

        foreach (var text in File.ReadLines(path))
        {
            try
            {
                if (JsonSerializer.Deserialize<Line>(text) is { } l)
                    _items.Add((l.K, new UsageSample(l.T, l.U, l.R)));
            }
            catch (JsonException)
            {
                // Skip corrupt lines; history is best-effort.
            }
        }
    }

    public IReadOnlyList<UsageSample> Samples(WindowKind kind) =>
        _items.Where(i => i.Kind == kind).Select(i => i.Sample).ToList();

    public void Add(WindowKind kind, UsageSample sample)
    {
        _items.Add((kind, sample));
        EnsureDirectory();
        File.AppendAllText(_path, Serialize(kind, sample) + Environment.NewLine);
    }

    public void Prune(DateTimeOffset olderThan)
    {
        if (_items.RemoveAll(i => i.Sample.At < olderThan) == 0)
            return;
        EnsureDirectory();
        File.WriteAllLines(_path, _items.Select(i => Serialize(i.Kind, i.Sample)));
    }

    private static string Serialize(WindowKind kind, UsageSample s) =>
        JsonSerializer.Serialize(new Line(kind, s.At, s.Utilization, s.ResetsAt));

    private void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
