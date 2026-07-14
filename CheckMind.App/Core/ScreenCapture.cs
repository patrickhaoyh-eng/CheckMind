using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CheckMind.App.Core;

public sealed class ScreenCapture
{
    public byte[] CaptureWindowPngBytes(IntPtr hWnd)
    {
        if (!Win32Native.GetWindowRect(hWnd, out var r))
        {
            throw new InvalidOperationException("GetWindowRect failed");
        }

        var width = Math.Max(1, r.Right - r.Left);
        var height = Math.Max(1, r.Bottom - r.Top);
        var screenBytes = CaptureRegionPngBytes(r.Left, r.Top, width, height);
        var windowBytes = TryCaptureWindowWithPrintWindow(hWnd, width, height);
        if (windowBytes is null)
        {
            return screenBytes;
        }

        return EstimateVisibleContentScore(windowBytes) >= EstimateVisibleContentScore(screenBytes)
            ? windowBytes
            : screenBytes;
    }

    public byte[] CaptureRegionPngBytes(int screenX, int screenY, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var hdcSrc = GetDC(IntPtr.Zero);
        if (hdcSrc == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetDC failed");
        }

        var hdcDest = CreateCompatibleDC(hdcSrc);
        if (hdcDest == IntPtr.Zero)
        {
            _ = ReleaseDC(IntPtr.Zero, hdcSrc);
            throw new InvalidOperationException("CreateCompatibleDC failed");
        }

        var hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
        if (hBitmap == IntPtr.Zero)
        {
            _ = DeleteDC(hdcDest);
            _ = ReleaseDC(IntPtr.Zero, hdcSrc);
            throw new InvalidOperationException("CreateCompatibleBitmap failed");
        }

        var hOld = SelectObject(hdcDest, hBitmap);
        try
        {
            if (!BitBlt(hdcDest, 0, 0, width, height, hdcSrc, screenX, screenY, SRCCOPY | CAPTUREBLT))
            {
                throw new InvalidOperationException("BitBlt failed");
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions()
            );

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        finally
        {
            _ = SelectObject(hdcDest, hOld);
            _ = DeleteObject(hBitmap);
            _ = DeleteDC(hdcDest);
            _ = ReleaseDC(IntPtr.Zero, hdcSrc);
        }
    }

    private static byte[]? TryCaptureWindowWithPrintWindow(IntPtr hWnd, int width, int height)
    {
        var hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
        {
            return null;
        }

        var hdcDest = CreateCompatibleDC(hdcScreen);
        if (hdcDest == IntPtr.Zero)
        {
            _ = ReleaseDC(IntPtr.Zero, hdcScreen);
            return null;
        }

        var hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
        if (hBitmap == IntPtr.Zero)
        {
            _ = DeleteDC(hdcDest);
            _ = ReleaseDC(IntPtr.Zero, hdcScreen);
            return null;
        }

        var hOld = SelectObject(hdcDest, hBitmap);
        try
        {
            var ok = PrintWindow(hWnd, hdcDest, PW_RENDERFULLCONTENT);
            if (!ok)
            {
                ok = PrintWindow(hWnd, hdcDest, 0);
            }

            if (!ok)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions()
            );

            return EncodeBitmapSourceToPng(source);
        }
        catch
        {
            return null;
        }
        finally
        {
            _ = SelectObject(hdcDest, hOld);
            _ = DeleteObject(hBitmap);
            _ = DeleteDC(hdcDest);
            _ = ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    private static byte[] EncodeBitmapSourceToPng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static int EstimateVisibleContentScore(byte[] imageBytes)
    {
        try
        {
            using var input = new MemoryStream(imageBytes, writable: false);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            BitmapSource src = frame;
            if (frame.Format != PixelFormats.Bgra32)
            {
                src = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            }

            var w = src.PixelWidth;
            var h = src.PixelHeight;
            if (w <= 0 || h <= 0)
            {
                return 0;
            }

            var stride = w * 4;
            var pixels = new byte[stride * h];
            src.CopyPixels(pixels, stride, 0);

            var count = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var b = pixels[i];
                var g = pixels[i + 1];
                var r = pixels[i + 2];
                var a = pixels[i + 3];
                var maxChannel = Math.Max(r, Math.Max(g, b));
                if (a >= 8 && maxChannel >= 12)
                {
                    count++;
                }
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }

    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const int PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdcDest,
        int nXDest,
        int nYDest,
        int nWidth,
        int nHeight,
        IntPtr hdcSrc,
        int nXSrc,
        int nYSrc,
        int dwRop
    );

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);
}
