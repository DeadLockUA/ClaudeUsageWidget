using System.Globalization;
using System.Text.Json;

namespace GitHubUsageWidget.Core;

/// <summary>One rate-limit window as reported by the server.</summary>
/// <param name="Utilization">Used share of the window, 0..100.</param>
/// <param name="ResetsAt">When the window resets; null when the window has not started.</param>
public sealed record LimitWindow(double Utilization, DateTimeOffset? ResetsAt);

public sealed record UsageSnapshot(DateTimeOffset FetchedAt, LimitWindow? FiveHour, LimitWindow? Weekly);

public enum WindowKind
{
    FiveHour,
    Weekly,
}

public static class WindowKindExtensions
{
    public static TimeSpan Length(this WindowKind kind) => kind switch
    {
        WindowKind.FiveHour => TimeSpan.FromHours(5),
        WindowKind.Weekly => TimeSpan.FromDays(7),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>Parses the response of GET https://api.anthropic.com/api/oauth/usage (undocumented endpoint).</summary>
public static class UsageParser
{
    public static UsageSnapshot Parse(string json, DateTimeOffset fetchedAt)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new UsageSnapshot(fetchedAt, ReadWindow(root, "five_hour"), ReadWindow(root, "seven_day"));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new FormatException("Unexpected usage response format.", ex);
        }
    }

    private static LimitWindow? ReadWindow(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object)
            return null;

        var utilization = w.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number
            ? u.GetDouble()
            : 0;

        DateTimeOffset? resetsAt = null;
        if (w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String)
            resetsAt = DateTimeOffset.Parse(r.GetString()!, CultureInfo.InvariantCulture);

        return new LimitWindow(utilization, resetsAt);
    }
}

public sealed record Credentials(string AccessToken, DateTimeOffset? ExpiresAt);

/// <summary>Reads the OAuth token that Claude Code stores locally. The widget never refreshes it.</summary>
public static class CredentialsReader
{
    public static Credentials Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object)
                throw new FormatException("No claudeAiOauth section in credentials file.");

            if (!oauth.TryGetProperty("accessToken", out var tok) || tok.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(tok.GetString()))
                throw new FormatException("No access token in credentials file.");

            DateTimeOffset? expiresAt = oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(exp.GetInt64())
                : null;

            return new Credentials(tok.GetString()!, expiresAt);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Credentials file is not valid JSON.", ex);
        }
    }

    public static string ResolvePath(string? configDir, string userProfile) =>
        string.IsNullOrWhiteSpace(configDir)
            ? Path.Combine(userProfile, ".claude", ".credentials.json")
            : Path.Combine(configDir, ".credentials.json");

    public static string DefaultPath() => ResolvePath(
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}
