namespace CheckMind.App.Core;

public static class OverlayRectCalculator
{
    public readonly record struct OverlayRectClampResult(
        BBox? Input,
        BBox? Clamped,
        BBox? MonitorBounds,
        DesktopPoint? ProbePoint,
        string Status
    );

    public static BBox? ClampToTargetScreen(BBox? rect)
        => ClampToTargetScreenWithDebug(rect).Clamped;

    public static OverlayRectClampResult ClampToTargetScreenWithDebug(BBox? rect)
    {
        if (rect is null || rect.Value.Width <= 0 || rect.Value.Height <= 0)
        {
            return new OverlayRectClampResult(rect, null, null, null, "empty");
        }

        var input = rect.Value;
        var probePoint = new DesktopPoint(
            input.X + Math.Max(1, input.Width / 2),
            input.Y + Math.Max(1, input.Height / 2)
        );
        var nativeProbePoint = new Win32Native.POINT
        {
            X = probePoint.X,
            Y = probePoint.Y
        };
        var monitor = Win32Native.MonitorFromPoint(nativeProbePoint, Win32Native.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return new OverlayRectClampResult(input, input, null, probePoint, "no-monitor");
        }

        var monitorInfo = new Win32Native.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32Native.MONITORINFO>()
        };
        if (!Win32Native.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return new OverlayRectClampResult(input, input, null, probePoint, "monitor-info-failed");
        }

        var bounds = monitorInfo.rcMonitor;
        var monitorBounds = new BBox(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);

        var left = Math.Max(input.X, bounds.Left);
        var top = Math.Max(input.Y, bounds.Top);
        var right = Math.Min(input.X + input.Width, bounds.Right);
        var bottom = Math.Min(input.Y + input.Height, bounds.Bottom);

        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return new OverlayRectClampResult(input, null, monitorBounds, probePoint, "clamped-empty");
        }

        return new OverlayRectClampResult(input, new BBox(left, top, width, height), monitorBounds, probePoint, "ok");
    }
}
