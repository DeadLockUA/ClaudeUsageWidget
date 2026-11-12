using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GitHubUsageWidget.Core;

namespace GitHubUsageWidget;

/// <summary>Popup with per-window details, shown above the strip; closes when it loses focus.</summary>
public partial class DetailsWindow : Window
{
    private readonly StripWindow _strip;
    private readonly WidgetController _controller;
    private bool _light;

    internal DetailsWindow(StripWindow strip, WidgetController controller)
    {
        InitializeComponent();
        _strip = strip;
        _controller = controller;
        Deactivated += (_, _) => Close();
        Loaded += (_, _) => PlaceAboveStrip();
        SizeChanged += (_, _) => PlaceAboveStrip();
        Render(controller.State);
    }

    internal void Render(WidgetState state)
    {
        _light = Theme.TaskbarIsLight();
        Frame.Background = Brush(_light ? 0xF9F9F9 : 0x202020);
        Frame.BorderBrush = Brush(_light ? 0xD0D0D0 : 0x3A3A3A);

        var now = DateTimeOffset.UtcNow;
        Body.Children.Clear();
        AddWindow("5-HOUR WINDOW", state.FiveHour, now, state.IsStale);
        AddWindow("WEEKLY", state.Weekly, now, state.IsStale);

        var footer = state.UpdatedAt is { } t
            ? $"Updated {DisplayFormatter.When(t, now, TimeZoneInfo.Local)}"
            : "No data yet";
        if (state.Problem is { } p)
            footer += " - " + p;
        Body.Children.Add(Text(footer, 11, Theme.Muted(_light), wrap: true));
    }

    private void AddWindow(string title, Forecast? f, DateTimeOffset now, bool stale)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        block.Children.Add(Text(title, 12, Theme.Muted(_light), bold: true));

        if (f is null)
        {
            block.Children.Add(Text("No data", 13, Theme.Muted(_light)));
            Body.Children.Add(block);
            return;
        }

        var verdictBrush = Theme.For(f.Verdict, _light, stale);

        block.Children.Add(Row(
            Text($"{f.RemainingPercent:0}% left", 14, Theme.Text(_light), bold: true),
            Text(ResetText(f, now), 12, Theme.Muted(_light))));

        var used = 100 - f.RemainingPercent;
        var track = new Grid { Height = 8, Margin = new Thickness(0, 6, 0, 6) };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(used, 0.001), GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - used, 0.001), GridUnitType.Star) });
        var bg = new Border { CornerRadius = new CornerRadius(4), Background = Brush(_light ? 0xE0E0E0 : 0x3A3A3A) };
        Grid.SetColumnSpan(bg, 2);
        track.Children.Add(bg);
        track.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = verdictBrush });
        block.Children.Add(track);

        block.Children.Add(Row(
            Text(PaceText(f), 12, Theme.Muted(_light)),
            Text(VerdictText(f, now), 12, verdictBrush, bold: true)));

        Body.Children.Add(block);
    }

    private static string ResetText(Forecast f, DateTimeOffset now) => f.ResetsAt is { } r
        ? $"resets {DisplayFormatter.When(r, now, TimeZoneInfo.Local)} (in {DisplayFormatter.Duration(r - now)})"
        : "not started";

    private static string PaceText(Forecast f)
    {
        if (f.RatePerHour is not { } rate)
            return "pace: not enough data";
        var value = f.Kind == WindowKind.Weekly
            ? (rate * 24).ToString("0.#", CultureInfo.InvariantCulture) + "%/day"
            : rate.ToString("0.#", CultureInfo.InvariantCulture) + "%/h";
        var source = f.RateSource == RateSource.Recent ? "last hour" : "window avg";
        return $"pace {value} ({source})";
    }

    private static string VerdictText(Forecast f, DateTimeOffset now) =>
        DisplayFormatter.DetailsVerdict(f, now, TimeZoneInfo.Local);

    private static Grid Row(UIElement left, UIElement right)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(right, 1);
        g.Children.Add(left);
        g.Children.Add(right);
        return g;
    }

    private static TextBlock Text(string s, double size, Brush brush, bool bold = false, bool wrap = false) => new()
    {
        Text = s,
        FontSize = size,
        Foreground = brush,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Bottom,
    };

    private static SolidColorBrush Brush(int rgb) =>
        new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private void PlaceAboveStrip()
    {
        var strip = _strip.ScreenRect();
        var dpi = VisualTreeHelper.GetDpi(this);
        var w = ActualWidth;
        var h = ActualHeight;

        // Convert the strip's pixel rect to DIPs and align our right edge with it.
        var right = strip.Right / dpi.DpiScaleX;
        var top = strip.Top / dpi.DpiScaleY;
        var screenLeft = SystemParameters.VirtualScreenLeft;

        Left = Math.Max(screenLeft, right - w);
        Top = top - h - 8;
    }
}
