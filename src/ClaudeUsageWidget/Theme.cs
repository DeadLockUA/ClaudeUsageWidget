using System.Windows.Media;
using ClaudeUsageWidget.Core;
using Microsoft.Win32;

namespace ClaudeUsageWidget;

/// <summary>Colors that follow the taskbar theme (SystemUsesLightTheme).</summary>
internal static class Theme
{
    public static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
    }

    public static Brush Text(bool light) => light ? Freeze(0x1A, 0x1A, 0x1A) : Freeze(0xF2, 0xF2, 0xF2);

    public static Brush Muted(bool light) => light ? Freeze(0x70, 0x70, 0x70) : Freeze(0x9A, 0x9A, 0x9A);

    public static Brush For(Verdict v, bool light, bool stale)
    {
        if (stale)
            return Muted(light);
        return v switch
        {
            Verdict.Ok => light ? Freeze(0x0F, 0x7B, 0x0F) : Freeze(0x46, 0xC4, 0x6E),
            Verdict.Tight => light ? Freeze(0x9D, 0x5D, 0x00) : Freeze(0xF5, 0xB1, 0x3D),
            Verdict.RunsOut or Verdict.Exhausted => light ? Freeze(0xC4, 0x2B, 0x1C) : Freeze(0xFF, 0x6B, 0x6E),
            _ => Muted(light),
        };
    }

    private static Brush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
