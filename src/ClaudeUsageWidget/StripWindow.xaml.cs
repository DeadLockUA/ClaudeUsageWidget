using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget;

/// <summary>The two-line strip that sits on the taskbar left of the notification area.</summary>
public partial class StripWindow : Window
{
    private const int GapPx = 4;
    private const double DragThresholdPx = 4;

    private readonly WidgetController _controller;
    private readonly Settings _settings = AppStorage.LoadSettings();
    private readonly DispatcherTimer _placeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Native.WinEventProc _foregroundProc; // kept alive for the native hook
    private IntPtr _hwnd;
    private IntPtr _hook;
    private bool _light;
    private DetailsWindow? _details;

    private Point? _dragStart;
    private int _dragStartOffset;
    private bool _dragging;

    internal StripWindow(WidgetController controller)
    {
        InitializeComponent();
        _controller = controller;
        _light = Theme.TaskbarIsLight();
        _foregroundProc = (_, _, _, _, _, _, _) => Place();

        _controller.StateChanged += _ => Render();
        _placeTimer.Tick += (_, _) => Place();
        _renderTimer.Tick += (_, _) =>
        {
            _light = Theme.TaskbarIsLight();
            Render();
        };
        SizeChanged += (_, _) => Place();
        Menu.Opened += (_, _) => AutostartItem.IsChecked = Autostart.IsEnabled;

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));

        // Clicking the taskbar raises it above us; re-assert top-most on every foreground change.
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _foregroundProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);

        Render();
        _placeTimer.Start();
        _renderTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hook != IntPtr.Zero)
            Native.UnhookWinEvent(_hook);
        _placeTimer.Stop();
        _renderTimer.Stop();
        base.OnClosed(e);
    }

    private void Render()
    {
        var state = _controller.State;
        var stale = state.IsStale;
        var now = DateTimeOffset.UtcNow;

        if (state.FiveHour is null && state.Weekly is null)
        {
            SetLine(Line1, "Claude", "", Theme.Muted(_light));
            SetLine(Line2, state.Problem ?? "no data", "", Theme.Muted(_light));
        }
        else
        {
            RenderLine(Line1, state.FiveHour, now, stale);
            RenderLine(Line2, state.Weekly, now, stale);
        }

        ToolTip = state.Problem is { } p
            ? $"{p}\nLast update: {Stamp(state.UpdatedAt, now)}"
            : $"Updated {Stamp(state.UpdatedAt, now)}. Click for details, drag to move.";

        _details?.Render(state);
    }

    private void RenderLine(System.Windows.Controls.TextBlock block, Forecast? f, DateTimeOffset now, bool stale)
    {
        if (f is null)
        {
            SetLine(block, "-", "", Theme.Muted(_light));
            return;
        }

        var text = DisplayFormatter.StripLine(f, now, TimeZoneInfo.Local);
        var split = text.IndexOf(' ', text.IndexOf(' ') + 1); // after "5h 89%"
        var head = split > 0 ? text[..split] : text;
        var tail = split > 0 ? text[split..] : "";
        SetLine(block, head, tail, Theme.For(f.Verdict, _light, stale), stale);
    }

    private void SetLine(System.Windows.Controls.TextBlock block, string head, string tail, Brush tailBrush, bool headMuted = false)
    {
        block.Inlines.Clear();
        block.Inlines.Add(new Run(head) { Foreground = headMuted ? Theme.Muted(_light) : Theme.Text(_light) });
        if (tail.Length > 0)
            block.Inlines.Add(new Run(tail) { Foreground = tailBrush });
    }

    private static string Stamp(DateTimeOffset? at, DateTimeOffset now) =>
        at is { } t ? DisplayFormatter.When(t, now, TimeZoneInfo.Local) : "never";

    /// <summary>Positions the strip over the taskbar and keeps it top-most; hides it when it should not show.</summary>
    private void Place()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        var info = TaskbarLocator.Find();
        if (info is null || info.IsHidden || TaskbarLocator.IsFullscreenAppActive(info))
        {
            if (IsVisible)
                Hide();
            return;
        }

        if (!IsVisible)
            Show();

        var dpi = VisualTreeHelper.GetDpi(this);
        var w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var maxOffset = Math.Max(0, info.Tray.Left - info.Taskbar.Left - w - GapPx);
        var offset = Math.Clamp(_settings.OffsetPx, 0, maxOffset);

        var x = info.Tray.Left - GapPx - offset - w;
        var y = info.Taskbar.Top + (info.Taskbar.Height - h) / 2;

        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    internal Native.RECT ScreenRect()
    {
        Native.GetWindowRect(_hwnd, out var r);
        return r;
    }

    internal void ShowDetails()
    {
        if (_details is { IsLoaded: true })
        {
            _details.Activate();
            return;
        }

        _details = new DetailsWindow(this, _controller);
        _details.Closed += (_, _) => _details = null;
        _details.Show();
    }

    private void ToggleDetails()
    {
        if (_details is { IsLoaded: true })
            _details.Close();
        else
            ShowDetails();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = PointToScreen(e.GetPosition(this));
        _dragStartOffset = _settings.OffsetPx;
        _dragging = false;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = PointToScreen(e.GetPosition(this));
        var delta = start.X - current.X; // moving left grows the offset
        if (!_dragging && Math.Abs(delta) < DragThresholdPx)
            return;

        _dragging = true;
        _settings.OffsetPx = Math.Max(0, _dragStartOffset + (int)Math.Round(delta));
        Place();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        var wasDragging = _dragging;
        _dragStart = null;
        _dragging = false;

        if (wasDragging)
            AppStorage.SaveSettings(_settings);
        else
            ToggleDetails();
    }

    private void OnDetails(object sender, RoutedEventArgs e) => ShowDetails();

    private async void OnRefresh(object sender, RoutedEventArgs e) => await _controller.RefreshAsync();

    private void OnAutostart(object sender, RoutedEventArgs e)
    {
        try
        {
            Autostart.Set(AutostartItem.IsChecked);
        }
        catch (Exception ex)
        {
            AppStorage.Log("Autostart: " + ex.Message);
            AutostartItem.IsChecked = Autostart.IsEnabled;
        }
    }

    private void OnResetPosition(object sender, RoutedEventArgs e)
    {
        _settings.OffsetPx = 0;
        AppStorage.SaveSettings(_settings);
        Place();
    }

    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
