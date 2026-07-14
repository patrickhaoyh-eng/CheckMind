using System.Threading;

namespace CheckMind.App.Core;

public sealed class WindowController
{
    public bool IsMaximized(IntPtr hWnd)
    {
        var placement = new Win32Native.WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<Win32Native.WINDOWPLACEMENT>() };
        if (!Win32Native.GetWindowPlacement(hWnd, ref placement))
        {
            return false;
        }

        return placement.showCmd == Win32Native.SW_MAXIMIZE;
    }

    public void Maximize(IntPtr hWnd)
    {
        _ = Win32Native.ShowWindow(hWnd, Win32Native.SW_MAXIMIZE);
    }

    public void Activate(IntPtr hWnd)
    {
        _ = Win32Native.SetForegroundWindow(hWnd);
    }

    public void ClickScreenPoint(int x, int y)
    {
        _ = Win32Native.SetCursorPos(x, y);
        Win32Native.mouse_event(Win32Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Win32Native.mouse_event(Win32Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public DesktopPoint? GetCursorScreenPoint()
    {
        if (!Win32Native.GetCursorPos(out var pt))
        {
            return null;
        }

        return new DesktopPoint(pt.X, pt.Y);
    }

    public IntPtr GetForegroundWindowHandle()
        => Win32Native.GetForegroundWindow();

    public IntPtr GetWindowFromScreenPoint(int x, int y)
        => Win32Native.WindowFromPoint(new Win32Native.POINT { X = x, Y = y });

    public void WheelAtScreenPoint(int x, int y, int delta)
    {
        _ = Win32Native.SetCursorPos(x, y);
        Win32Native.mouse_event(Win32Native.MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
    }

    public void PressPageDown()
    {
        PressPageDown(0);
    }

    public void PressPageDown(int keyDelayMs)
    {
        Win32Native.keybd_event(Win32Native.VK_NEXT, 0, 0, UIntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }

        Win32Native.keybd_event(Win32Native.VK_NEXT, 0, Win32Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }
    }

    public void PressPageUp()
    {
        PressPageUp(0);
    }

    public void PressPageUp(int keyDelayMs)
    {
        Win32Native.keybd_event(Win32Native.VK_PRIOR, 0, 0, UIntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }

        Win32Native.keybd_event(Win32Native.VK_PRIOR, 0, Win32Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }
    }

    public void DragScreenPoint(int fromX, int fromY, int toX, int toY, int pauseMs = 60)
    {
        _ = Win32Native.SetCursorPos(fromX, fromY);
        Win32Native.mouse_event(Win32Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(pauseMs);
        _ = Win32Native.SetCursorPos(toX, toY);
        Thread.Sleep(pauseMs);
        Win32Native.mouse_event(Win32Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(pauseMs);
    }
}
