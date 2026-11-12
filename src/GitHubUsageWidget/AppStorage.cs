using System.IO;
using System.Text.Json;
using GitHubUsageWidget.Core;
using Microsoft.Win32;

namespace GitHubUsageWidget;

internal sealed class Settings
{
    /// <summary>Extra gap in physical pixels between the strip and the notification area.</summary>
    public int OffsetPx { get; set; }
}

/// <summary>Files under %LOCALAPPDATA%\GitHubUsageWidget.</summary>
internal static class AppStorage
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitHubUsageWidget");

    public static string HistoryPath => Path.Combine(Dir, "history.jsonl");
    private static string SettingsPath => Path.Combine(Dir, "settings.json");
    private static string AlertStatePath => Path.Combine(Dir, "alert-state.json");
    private static string LogPath => Path.Combine(Dir, "log.txt");

    public static Settings LoadSettings() => Load<Settings>(SettingsPath) ?? new Settings();

    public static void SaveSettings(Settings s) => Save(SettingsPath, s);

    public static AlertState? LoadAlertState() => Load<AlertState>(AlertStatePath);

    public static void SaveAlertState(AlertState s) => Save(AlertStatePath, s);

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > 1_000_000)
                info.Delete();
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging must never take the widget down.
        }
    }

    private static T? Load<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log($"Failed to read {path}: {ex.Message}");
            return null;
        }
    }

    private static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(path, JsonSerializer.Serialize(value));
        }
        catch (IOException ex)
        {
            Log($"Failed to write {path}: {ex.Message}");
        }
    }
}

/// <summary>"Start with Windows" via the per-user Run key.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GitHubUsageWidget";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
