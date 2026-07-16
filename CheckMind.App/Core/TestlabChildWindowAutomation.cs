using System.Text;

namespace CheckMind.App.Core;

public sealed class TestlabChildWindowAutomation
{
    private readonly TestlabChildWindowLocator _locator;
    private readonly WindowController _controller;

    public TestlabChildWindowAutomation(TestlabChildWindowLocator locator, WindowController controller)
    {
        _locator = locator;
        _controller = controller;
    }

    public static WindowPoint ComputeIndexedRowClickPoint(WorkstationListNavigationTarget listTarget, int oneBasedIndex)
    {
        if (oneBasedIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(oneBasedIndex), "序号必须从 1 开始。");
        }

        if (listTarget.RoiWindow is not BBox roi)
        {
            throw new InvalidOperationException($"列表导航目标 [{listTarget.Key}] 缺少 RoiWindow。");
        }

        if (listTarget.FirstRowAnchor is not WindowPoint firstRowAnchor)
        {
            throw new InvalidOperationException($"列表导航目标 [{listTarget.Key}] 缺少 FirstRowAnchor。");
        }

        if (listTarget.RowHeight is not int rowHeight || rowHeight <= 0)
        {
            throw new InvalidOperationException($"列表导航目标 [{listTarget.Key}] 缺少有效的 RowHeight。");
        }

        var rowPoint = new WindowPoint(
            X: firstRowAnchor.X,
            Y: firstRowAnchor.Y + ((oneBasedIndex - 1) * rowHeight)
        );

        if (!IsPointWithinRoi(rowPoint, roi))
        {
            throw new InvalidOperationException(
                $"目标序号 [{oneBasedIndex}] 的行点击点超出列表 ROI。point=({rowPoint.X},{rowPoint.Y}), roi=({roi.X},{roi.Y},{roi.Width},{roi.Height})"
            );
        }

        return rowPoint;
    }

    public TestlabChildWindowOpenResult OpenChildWindowFromIndexedListEntry(
        TestlabWindowInfo listWindow,
        WorkstationListNavigationTarget listTarget,
        int oneBasedIndex,
        string childWindowTitleContains,
        bool maximizeChildWindow,
        int listSelectionDelayMs = 120,
        int childWindowAppearTimeoutMs = 3000
    )
    {
        if (string.IsNullOrWhiteSpace(childWindowTitleContains))
        {
            throw new ArgumentException("子窗口标题关键字不能为空。", nameof(childWindowTitleContains));
        }

        if (listTarget.ActionClickPoint is not WindowPoint actionClickPoint)
        {
            throw new InvalidOperationException($"列表导航目标 [{listTarget.Key}] 缺少 ActionClickPoint。");
        }

        var rowClickPoint = ComputeIndexedRowClickPoint(listTarget, oneBasedIndex);

        _controller.Activate(listWindow.Hwnd);
        Thread.Sleep(80);
        _controller.ClickWindowPoint(listWindow.Hwnd, rowClickPoint);
        Thread.Sleep(Math.Max(0, listSelectionDelayMs));
        _controller.ClickWindowPoint(listWindow.Hwnd, actionClickPoint);

        var childWindow = _locator.FindByTitleContains(
            childWindowTitleContains,
            processName: listWindow.ProcessName,
            timeoutMs: childWindowAppearTimeoutMs
        );

        if (maximizeChildWindow)
        {
            _controller.Activate(childWindow.Hwnd);
            Thread.Sleep(60);
            _controller.MaximizeForegroundWindowBySystemMenu();
            Thread.Sleep(250);
            childWindow = _locator.FindByTitleContains(
                childWindowTitleContains,
                processName: listWindow.ProcessName,
                timeoutMs: 1000
            );
        }

        return new TestlabChildWindowOpenResult(
            ListWindowTitle: listWindow.Title,
            ChildWindowTitle: childWindow.Title,
            RowIndex: oneBasedIndex,
            RowClickPointWindow: rowClickPoint,
            ActionClickPointWindow: actionClickPoint,
            ChildWindowRect: childWindow.Rect
        );
    }

    private static bool IsPointWithinRoi(WindowPoint point, BBox roi)
        => point.X >= roi.X &&
           point.Y >= roi.Y &&
           point.X < (roi.X + roi.Width) &&
           point.Y < (roi.Y + roi.Height);
}

public sealed class TestlabChildWindowLocator
{
    public TestlabWindowInfo FindByTitleContains(string titleContains, string? processName = null, int timeoutMs = 2000)
    {
        var normalizedProcessName = string.IsNullOrWhiteSpace(processName)
            ? null
            : processName.Trim();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < Math.Max(200, timeoutMs))
        {
            var found = TryFind(titleContains, normalizedProcessName);
            if (found is not null)
            {
                return found.Value;
            }

            Thread.Sleep(100);
        }

        if (!string.IsNullOrWhiteSpace(normalizedProcessName))
        {
            var fallback = TryFind(titleContains, processName: null);
            if (fallback is not null)
            {
                return fallback.Value;
            }
        }

        throw new InvalidOperationException(
            $"未找到目标子窗口。titleContains={titleContains};process={(normalizedProcessName ?? "<same-as-main>")}"
        );
    }

    public TestlabWindowInfo? TryFindByTitleContains(string titleContains, string? processName = null)
    {
        var normalizedProcessName = string.IsNullOrWhiteSpace(processName)
            ? null
            : processName.Trim();
        var found = TryFind(titleContains, normalizedProcessName);
        if (found is not null)
        {
            return found.Value;
        }

        if (!string.IsNullOrWhiteSpace(normalizedProcessName))
        {
            var fallback = TryFind(titleContains, processName: null);
            if (fallback is not null)
            {
                return fallback.Value;
            }
        }

        return null;
    }

    private static TestlabWindowInfo? TryFind(string titleContains, string? processName)
    {
        TestlabWindowInfo? found = null;
        Win32Native.EnumWindows((hWnd, _) =>
        {
            if (!Win32Native.IsWindowVisible(hWnd))
            {
                return true;
            }

            var title = Win32Native.GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title) ||
                !title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Win32Native.GetWindowThreadProcessId(hWnd, out var pid);
            var exe = Win32Native.TryGetProcessName(pid) ?? "";
            if (!string.IsNullOrWhiteSpace(processName) &&
                !exe.EndsWith(processName, StringComparison.OrdinalIgnoreCase))
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

        return found;
    }
}

public sealed record TestlabChildWindowOpenResult(
    string ListWindowTitle,
    string ChildWindowTitle,
    int RowIndex,
    WindowPoint RowClickPointWindow,
    WindowPoint ActionClickPointWindow,
    DesktopRect ChildWindowRect
)
{
    public string ToDebugText()
        => $"list={ListWindowTitle};child={ChildWindowTitle};row={RowIndex};rowPoint=({RowClickPointWindow.X},{RowClickPointWindow.Y});action=({ActionClickPointWindow.X},{ActionClickPointWindow.Y});childRect=({ChildWindowRect.Left},{ChildWindowRect.Top},{ChildWindowRect.Width},{ChildWindowRect.Height})";
}
