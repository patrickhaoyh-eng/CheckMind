using System.Windows;
using CheckMind.App.Core;

namespace CheckMind.App.Ui;

public sealed class CaptureOverlayService : ICaptureOverlay, IDisposable
{
    private CaptureOverlayWindow? _window;

    public void Start()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window is not null)
            {
                return;
            }

            _window = new CaptureOverlayWindow
            {
                ShowActivated = false
            };
            _window.Show();
            _window.Hide();
        });
    }

    public void SetVisible(bool visible)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window is null)
            {
                return;
            }

            if (visible)
            {
                _window.Show();
            }
            else
            {
                _window.Hide();
            }
        });
    }

    public void SetRect(BBox? rect)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var clamp = OverlayRectCalculator.ClampToTargetScreenWithDebug(rect);
            TestlabDebugMarkers.WritePhase(
                "overlay.service_set_rect",
                detail: $"status={clamp.Status};raw={FormatBBox(clamp.Input)};clamped={FormatBBox(clamp.Clamped)};monitor={FormatBBox(clamp.MonitorBounds)};probe={FormatPoint(clamp.ProbePoint)}"
            );
            _window?.SetRect(clamp.Clamped, clamp.MonitorBounds);
        });
    }

    public void Dispose()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window is null)
            {
                return;
            }

            _window.Close();
            _window = null;
        });
    }

    private static string FormatBBox(BBox? rect)
        => rect is null ? "null" : $"({rect.Value.X},{rect.Value.Y},{rect.Value.Width},{rect.Value.Height})";

    private static string FormatPoint(DesktopPoint? point)
        => point is null ? "null" : $"({point.Value.X},{point.Value.Y})";
}
