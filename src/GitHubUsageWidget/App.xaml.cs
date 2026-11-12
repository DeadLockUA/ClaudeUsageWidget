using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GitHubUsageWidget.Core;

namespace GitHubUsageWidget;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private WidgetController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\GitHubUsageWidget.SingleInstance", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => AppStorage.Log("Fatal: " + ev.ExceptionObject);

        _controller = new WidgetController();
        var strip = new StripWindow(_controller);
        _controller.AlertRaised += alert => ShowAlert(alert, strip);
        strip.Show();
        _controller.Start();

        if (e.Args.Contains("--demo-alert"))
            ShowAlert(new Alert(WindowKind.FiveHour, TimeSpan.FromHours(1), TimeSpan.FromMinutes(55),
                TimeSpan.FromHours(2.5), DateTimeOffset.UtcNow.AddHours(2.5)), strip);
    }

    private static void ShowAlert(Alert alert, StripWindow strip) => new AlertWindow(alert, strip.ShowDetails).Show();

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppStorage.Log("Unhandled: " + e.Exception);
        e.Handled = true; // a widget should survive a glitch rather than vanish
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
