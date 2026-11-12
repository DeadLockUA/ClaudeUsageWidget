namespace ClaudeUsageWidget;

/// <summary>Geometry of the primary taskbar, in physical pixels.</summary>
internal sealed record TaskbarInfo(Native.RECT Taskbar, Native.RECT Tray, Native.RECT Monitor, bool AutoHide)
{
    /// <summary>Auto-hidden taskbar slid off screen (only a few pixels remain visible).</summary>
    public bool IsHidden => AutoHide && Taskbar.Top >= Monitor.Bottom - 8;
}

/// <summary>
/// Windows 11 has no supported way to embed into the taskbar, so the widget is a top-most
/// window placed over the empty taskbar area left of the notification area (TrayNotifyWnd).
/// </summary>
internal static class TaskbarLocator
{
    /// <summary>Fallback width of the notification area when TrayNotifyWnd is missing.</summary>
    private const int FallbackTrayWidth = 260;

    public static TaskbarInfo? Find()
    {
        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !Native.GetWindowRect(taskbar, out var tbRect))
            return null;

        var monitor = Native.MonitorFromWindow(taskbar, Native.MONITOR_DEFAULTTOPRIMARY);
        var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref mi))
            return null;

        var trayHwnd = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        Native.RECT tray;
        if (trayHwnd == IntPtr.Zero || !Native.GetWindowRect(trayHwnd, out tray) || tray.Width <= 0)
            tray = new Native.RECT { Left = tbRect.Right - FallbackTrayWidth, Top = tbRect.Top, Right = tbRect.Right, Bottom = tbRect.Bottom };

        var abd = new Native.APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.APPBARDATA>() };
        var state = (uint)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd);

        return new TaskbarInfo(tbRect, tray, mi.rcMonitor, (state & Native.ABS_AUTOHIDE) != 0);
    }

    /// <summary>True when the foreground window covers its whole monitor (game, video, presentation).</summary>
    public static bool IsFullscreenAppActive(TaskbarInfo info)
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero)
            return false;

        var cls = Native.ClassOf(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        if (!Native.GetWindowRect(fg, out var r))
            return false;

        var m = info.Monitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }
}
