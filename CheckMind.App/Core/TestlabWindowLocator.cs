namespace CheckMind.App.Core;

public sealed class TestlabWindowLocator
{
    public TestlabWindowInfo Find()
    {
        var processName = Environment.GetEnvironmentVariable("CHECKMIND_TESTLAB_PROCESS");
        if (string.IsNullOrWhiteSpace(processName))
        {
            processName = "LmsHq_ActiveCompVC_LmsLoader_v5.exe";
        }

        var titleContains = Environment.GetEnvironmentVariable("CHECKMIND_TESTLAB_TITLE_CONTAINS");
        if (string.IsNullOrWhiteSpace(titleContains))
        {
            titleContains = "Simcenter Testlab";
        }

        TestlabWindowInfo? found = null;

        Win32Native.EnumWindows((hWnd, lParam) =>
        {
            if (!Win32Native.IsWindowVisible(hWnd))
            {
                return true;
            }

            var title = Win32Native.GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            Win32Native.GetWindowThreadProcessId(hWnd, out var pid);
            var exe = Win32Native.TryGetProcessName(pid) ?? "";

            if (!exe.EndsWith(processName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!Win32Native.GetWindowRect(hWnd, out var r))
            {
                return true;
            }

            found = new TestlabWindowInfo(
                Hwnd: hWnd,
                ProcessId: pid,
                ProcessName: exe,
                Title: title,
                Rect: new DesktopRect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top))
            );
            return false;
        }, IntPtr.Zero);

        if (found is null)
        {
            throw new InvalidOperationException($"未找到 Testlab 主窗口。process={processName}, titleContains={titleContains}");
        }

        if (found.Value.Rect.IsClearlyMinimizedPlacement)
        {
            throw new TestlabWindowStateException(
                $"检测到 Testlab 窗口处于最小化或不可抓取状态。当前坐标 Left={found.Value.Rect.Left}, Top={found.Value.Rect.Top}, Width={found.Value.Rect.Width}, Height={found.Value.Rect.Height}。请先将 Testlab 主窗口恢复到前台正常显示后再重试。"
            );
        }

        return found.Value;
    }
}

public readonly record struct TestlabWindowInfo(
    IntPtr Hwnd,
    uint ProcessId,
    string ProcessName,
    string Title,
    DesktopRect Rect
);
