using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CheckMind.App.Core;

namespace CheckMind.App.Ui;

public partial class CaptureOverlayWindow : System.Windows.Window
{
    private BBox? _currentMonitorBounds;

    public CaptureOverlayWindow()
    {
        InitializeComponent();
        Left = 0;
        Top = 0;
        Width = 1;
        Height = 1;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        ex = new IntPtr(ex.ToInt64() | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, ex);
    }

    public void SetRect(BBox? rect, BBox? monitorBounds = null)
    {
        if (monitorBounds is not null)
        {
            ApplyMonitorBounds(monitorBounds.Value);
        }

        if (rect is null || rect.Value.Width <= 0 || rect.Value.Height <= 0)
        {
            TestlabDebugMarkers.WritePhase("overlay.window_set_rect", detail: "collapsed");
            BorderRect.Visibility = Visibility.Collapsed;
            return;
        }

        BorderRect.Visibility = Visibility.Visible;
        var topLeft = PointFromScreen(new System.Windows.Point(rect.Value.X, rect.Value.Y));
        var bottomRight = PointFromScreen(new System.Windows.Point(rect.Value.X + rect.Value.Width, rect.Value.Y + rect.Value.Height));
        var dx = topLeft.X;
        var dy = topLeft.Y;
        var width = Math.Max(0, bottomRight.X - topLeft.X);
        var height = Math.Max(0, bottomRight.Y - topLeft.Y);
        Canvas.SetLeft(BorderRect, dx);
        Canvas.SetTop(BorderRect, dy);
        BorderRect.Width = width;
        BorderRect.Height = height;
        TestlabDebugMarkers.WritePhase(
            "overlay.window_set_rect",
            detail: $"rect=({rect.Value.X},{rect.Value.Y},{rect.Value.Width},{rect.Value.Height});canvas=({dx},{dy},{BorderRect.Width},{BorderRect.Height});monitor={FormatBBox(_currentMonitorBounds)};window=({Left},{Top},{Width},{Height})"
        );
    }

    private void ApplyMonitorBounds(BBox monitorBounds)
    {
        if (_currentMonitorBounds == monitorBounds)
        {
            return;
        }

        _currentMonitorBounds = monitorBounds;
        var hwnd = new WindowInteropHelper(this).Handle;
        _ = Win32Native.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            monitorBounds.X,
            monitorBounds.Y,
            monitorBounds.Width,
            monitorBounds.Height,
            Win32Native.SWP_NOACTIVATE | Win32Native.SWP_NOZORDER | Win32Native.SWP_NOOWNERZORDER
        );

        var topLeft = PointFromScreen(new System.Windows.Point(monitorBounds.X, monitorBounds.Y));
        var bottomRight = PointFromScreen(new System.Windows.Point(monitorBounds.X + monitorBounds.Width, monitorBounds.Y + monitorBounds.Height));
        Width = Math.Max(1, bottomRight.X - topLeft.X);
        Height = Math.Max(1, bottomRight.Y - topLeft.Y);
        Root.Width = Width;
        Root.Height = Height;

        TestlabDebugMarkers.WritePhase(
            "overlay.window_monitor_bounds",
            detail: $"monitor={FormatBBox(monitorBounds)};window=({Width},{Height})"
        );
    }

    private static string FormatBBox(BBox? rect)
        => rect is null ? "null" : $"({rect.Value.X},{rect.Value.Y},{rect.Value.Width},{rect.Value.Height})";

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_LAYERED = 0x00080000L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
