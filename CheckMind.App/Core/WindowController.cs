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

    public void MaximizeForegroundWindow()
    {
        var hWnd = GetForegroundWindowHandle();
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        Maximize(hWnd);
    }

    public void MaximizeForegroundWindowBySystemMenu(int keyDelayMs = 60)
    {
        var hWnd = GetForegroundWindowHandle();
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        Activate(hWnd);
        Thread.Sleep(Math.Max(0, keyDelayMs));
        PressChord(Win32Native.VK_MENU, Win32Native.VK_SPACE, keyDelayMs);
        PressKey(Win32Native.VK_X, keyDelayMs);
        Thread.Sleep(Math.Max(0, keyDelayMs));
    }

    public void CloseForegroundWindowByShortcut(int keyDelayMs = 60)
    {
        var hWnd = GetForegroundWindowHandle();
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        Activate(hWnd);
        Thread.Sleep(Math.Max(0, keyDelayMs));
        PressChord(Win32Native.VK_MENU, Win32Native.VK_F4, keyDelayMs);
        Thread.Sleep(Math.Max(0, keyDelayMs));
    }

    public void ClickScreenPoint(int x, int y)
    {
        _ = Win32Native.SetCursorPos(x, y);
        Win32Native.mouse_event(Win32Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Win32Native.mouse_event(Win32Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public void ClickScreenPointBySendInput(int x, int y, int buttonPauseMs = 20)
    {
        _ = Win32Native.SetCursorPos(x, y);
        if (buttonPauseMs > 0)
        {
            Thread.Sleep(buttonPauseMs);
        }

        SendMouseInput(Win32Native.MOUSEEVENTF_LEFTDOWN);
        if (buttonPauseMs > 0)
        {
            Thread.Sleep(buttonPauseMs);
        }

        SendMouseInput(Win32Native.MOUSEEVENTF_LEFTUP);
    }

    public void ClickWindowPoint(IntPtr hWnd, WindowPoint point)
    {
        var rect = GetWindowRect(hWnd);
        ClickScreenPoint(rect.Left + point.X, rect.Top + point.Y);
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

    public DesktopRect GetWindowRect(IntPtr hWnd)
    {
        if (!Win32Native.GetWindowRect(hWnd, out var r))
        {
            return new DesktopRect(0, 0, 0, 0);
        }

        return new DesktopRect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
    }

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

    public void PressPageDownToWindow(IntPtr hWnd, int keyDelayMs)
    {
        if (hWnd == IntPtr.Zero)
        {
            PressPageDown(keyDelayMs);
            return;
        }

        _ = Win32Native.PostMessage(hWnd, Win32Native.WM_KEYDOWN, (IntPtr)Win32Native.VK_NEXT, IntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }

        _ = Win32Native.PostMessage(hWnd, Win32Native.WM_KEYUP, (IntPtr)Win32Native.VK_NEXT, IntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }
    }

    public void PressPageDownToForegroundWindow(int keyDelayMs)
    {
        var hWnd = GetForegroundWindowHandle();
        if (hWnd != IntPtr.Zero)
        {
            Activate(hWnd);
            if (keyDelayMs > 0)
            {
                Thread.Sleep(keyDelayMs);
            }
        }

        PressPageDown(keyDelayMs);
    }

    public void PressPageDownToForegroundWindowBySendInput(int keyDelayMs)
    {
        var hWnd = GetForegroundWindowHandle();
        if (hWnd != IntPtr.Zero)
        {
            Activate(hWnd);
            if (keyDelayMs > 0)
            {
                Thread.Sleep(keyDelayMs);
            }
        }

        SendKeyboardInput(Win32Native.VK_NEXT, keyUp: false);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }

        SendKeyboardInput(Win32Native.VK_NEXT, keyUp: true);
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

    public void PressPageUpToWindow(IntPtr hWnd, int keyDelayMs)
    {
        if (hWnd == IntPtr.Zero)
        {
            PressPageUp(keyDelayMs);
            return;
        }

        _ = Win32Native.PostMessage(hWnd, Win32Native.WM_KEYDOWN, (IntPtr)Win32Native.VK_PRIOR, IntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }

        _ = Win32Native.PostMessage(hWnd, Win32Native.WM_KEYUP, (IntPtr)Win32Native.VK_PRIOR, IntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }
    }

    public void PressPageUpToForegroundWindow(int keyDelayMs)
    {
        var hWnd = GetForegroundWindowHandle();
        if (hWnd != IntPtr.Zero)
        {
            Activate(hWnd);
            if (keyDelayMs > 0)
            {
                Thread.Sleep(keyDelayMs);
            }
        }

        PressPageUp(keyDelayMs);
    }

    public void PressPageUpToForegroundWindowBySendInput(int keyDelayMs)
    {
        var hWnd = GetForegroundWindowHandle();
        if (hWnd != IntPtr.Zero)
        {
            Activate(hWnd);
            if (keyDelayMs > 0)
            {
                Thread.Sleep(keyDelayMs);
            }
        }

        SendKeyboardInput(Win32Native.VK_PRIOR, keyUp: false);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }

        SendKeyboardInput(Win32Native.VK_PRIOR, keyUp: true);
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

    private static void PressChord(byte modifier, byte key, int keyDelayMs)
    {
        Win32Native.keybd_event(modifier, 0, 0, UIntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }

        Win32Native.keybd_event(key, 0, 0, UIntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }

        Win32Native.keybd_event(key, 0, Win32Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }

        Win32Native.keybd_event(modifier, 0, Win32Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }
    }

    private static void PressKey(byte key, int keyDelayMs)
    {
        Win32Native.keybd_event(key, 0, 0, UIntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }

        Win32Native.keybd_event(key, 0, Win32Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (keyDelayMs > 0)
        {
            Thread.Sleep(keyDelayMs);
        }
    }

    private static void SendMouseInput(uint flags)
    {
        var inputs = new[]
        {
            new Win32Native.INPUT
            {
                type = Win32Native.INPUT_MOUSE,
                U = new Win32Native.InputUnion
                {
                    mi = new Win32Native.MOUSEINPUT
                    {
                        dwFlags = flags
                    }
                }
            }
        };

        _ = Win32Native.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Win32Native.INPUT>());
    }

    private static void SendKeyboardInput(byte key, bool keyUp)
    {
        var inputs = new[]
        {
            new Win32Native.INPUT
            {
                type = Win32Native.INPUT_KEYBOARD,
                U = new Win32Native.InputUnion
                {
                    ki = new Win32Native.KEYBDINPUT
                    {
                        wVk = key,
                        dwFlags = keyUp ? Win32Native.KEYEVENTF_KEYUP : 0
                    }
                }
            }
        };

        _ = Win32Native.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Win32Native.INPUT>());
    }
}
