using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GitHubUsageWidget.Core;

namespace GitHubUsageWidget;

/// <summary>Toast-like popup above the notification area. Click opens details; auto-closes.</summary>
public partial class AlertWindow : Window
{
    private static readonly TimeSpan AutoClose = TimeSpan.FromMinutes(2);
    private readonly DispatcherTimer _closeTimer = new() { Interval = AutoClose };

    internal AlertWindow(Alert alert, Action openDetails)
    {
        InitializeComponent();
        var light = Theme.TaskbarIsLight();
        Frame.Background = new SolidColorBrush(light ? Color.FromRgb(0xF9, 0xF9, 0xF9) : Color.FromRgb(0x20, 0x20, 0x20));
        Frame.BorderBrush = Theme.For(Verdict.RunsOut, light, stale: false);
        TitleText.Foreground = Theme.For(Verdict.RunsOut, light, stale: false);
        BodyText.Foreground = Theme.Text(light);
        CloseButton.Foreground = Theme.Muted(light);

        var now = DateTimeOffset.UtcNow;
        TitleText.Text = "Claude 5-hour limit";
        BodyText.Text =
            $"~{DisplayFormatter.Duration(alert.TimeToLimit)} left at the current pace. " +
            $"Resets {DisplayFormatter.When(alert.ResetsAt, now, TimeZoneInfo.Local)} " +
            $"(in {DisplayFormatter.Duration(alert.ResetsAt - now)}).";

        CloseButton.Click += (_, e) =>
        {
            e.Handled = true;
            Close();
        };
        Frame.MouseLeftButtonUp += (_, _) =>
        {
            Close();
            openDetails();
        };
        _closeTimer.Tick += (_, _) => Close();
        Loaded += (_, _) => Place();
        Closed += (_, _) => _closeTimer.Stop();
        _closeTimer.Start();
    }

    private void Place()
    {
        var area = SystemParameters.WorkArea; // primary monitor work area, in DIPs
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
    }
}
