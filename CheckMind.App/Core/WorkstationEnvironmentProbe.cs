using System.Linq;
using System.Windows.Forms;

namespace CheckMind.App.Core;

public sealed record WorkstationMeasuredEnvironment(
    int MonitorCount,
    int WindowMonitorIndex,
    int WindowMonitorWidth,
    int WindowMonitorHeight,
    int DpiScalePercent,
    uint RawDpi,
    bool IsMaximized,
    BBox WindowRectScreen,
    IReadOnlyList<WorkstationMonitorSnapshot> Win32Monitors,
    IReadOnlyList<WorkstationMonitorSnapshot> WinFormsMonitors
);

public sealed record WorkstationMonitorSnapshot(
    int Index,
    string Source,
    string? DeviceName,
    bool IsPrimary,
    BBox Bounds
);

public sealed class WorkstationEnvironmentProbe
{
    public WorkstationMeasuredEnvironment Probe(IntPtr hWnd)
    {
        var win32Monitors = GetWin32Monitors();
        var winFormsMonitors = GetWinFormsMonitors();
        var (monitorCount, windowMonitorIndex, windowMonitorBounds) = GetMonitorInfoSnapshot(hWnd, win32Monitors);
        var rawDpi = Win32Native.GetDpiForWindow(hWnd);
        var dpiScale = GetDpiScalePercent(rawDpi);
        var isMaximized = IsWindowMaximized(hWnd);
        var rect = GetWindowRect(hWnd);

        return new WorkstationMeasuredEnvironment(
            MonitorCount: monitorCount,
            WindowMonitorIndex: windowMonitorIndex,
            WindowMonitorWidth: windowMonitorBounds.Width,
            WindowMonitorHeight: windowMonitorBounds.Height,
            DpiScalePercent: dpiScale,
            RawDpi: rawDpi,
            IsMaximized: isMaximized,
            WindowRectScreen: rect,
            Win32Monitors: win32Monitors,
            WinFormsMonitors: winFormsMonitors
        );
    }

    private static (int MonitorCount, int WindowMonitorIndex, BBox WindowMonitorBounds) GetMonitorInfoSnapshot(
        IntPtr hWnd,
        IReadOnlyList<WorkstationMonitorSnapshot> monitors
    )
    {
        var windowRect = GetWindowRect(hWnd);
        var monitorIndex = -1;
        var bestOverlapArea = -1L;
        for (var i = 0; i < monitors.Count; i++)
        {
            var overlapArea = GetIntersectionArea(monitors[i].Bounds, windowRect);
            if (overlapArea > bestOverlapArea)
            {
                monitorIndex = i;
                bestOverlapArea = overlapArea;
            }
        }

        if (monitorIndex < 0 || bestOverlapArea <= 0)
        {
            monitorIndex = 0;
        }

        var target = monitorIndex < monitors.Count ? monitors[monitorIndex] : null;
        var bounds = target is null
            ? new BBox(0, 0, 0, 0)
            : target.Bounds;

        return (monitors.Count, monitorIndex + 1, bounds);
    }

    private static IReadOnlyList<WorkstationMonitorSnapshot> GetWin32Monitors()
        => Win32Native.EnumerateMonitors()
            .Select((monitor, index) => new WorkstationMonitorSnapshot(
                Index: index + 1,
                Source: "win32",
                DeviceName: null,
                IsPrimary: monitor.IsPrimary,
                Bounds: new BBox(
                    monitor.Bounds.Left,
                    monitor.Bounds.Top,
                    monitor.Bounds.Right - monitor.Bounds.Left,
                    monitor.Bounds.Bottom - monitor.Bounds.Top
                )
            ))
            .ToArray();

    private static IReadOnlyList<WorkstationMonitorSnapshot> GetWinFormsMonitors()
        => Screen.AllScreens
            .Select((screen, index) => new WorkstationMonitorSnapshot(
                Index: index + 1,
                Source: "winforms",
                DeviceName: screen.DeviceName,
                IsPrimary: screen.Primary,
                Bounds: new BBox(
                    screen.Bounds.X,
                    screen.Bounds.Y,
                    screen.Bounds.Width,
                    screen.Bounds.Height
                )
            ))
            .ToArray();

    private static int GetDpiScalePercent(uint dpi)
    {
        if (dpi <= 0)
        {
            return 100;
        }

        return (int)Math.Round(dpi * 100.0 / 96.0);
    }

    private static bool IsWindowMaximized(IntPtr hWnd)
    {
        var placement = new Win32Native.WINDOWPLACEMENT();
        placement.length = System.Runtime.InteropServices.Marshal.SizeOf<Win32Native.WINDOWPLACEMENT>();
        if (!Win32Native.GetWindowPlacement(hWnd, ref placement))
        {
            return false;
        }

        return placement.showCmd == Win32Native.SW_MAXIMIZE;
    }

    private static BBox GetWindowRect(IntPtr hWnd)
    {
        if (!Win32Native.GetWindowRect(hWnd, out var r))
        {
            return new BBox(0, 0, 0, 0);
        }

        return new BBox(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private static long GetIntersectionArea(BBox a, BBox b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        if (right <= left || bottom <= top)
        {
            return 0;
        }

        return (long)(right - left) * (bottom - top);
    }
}
