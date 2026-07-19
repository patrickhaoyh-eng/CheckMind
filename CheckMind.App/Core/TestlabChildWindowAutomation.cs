using System.Security.Cryptography;
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
            throw new ListEntryOutOfRangeException(oneBasedIndex, rowPoint, roi);
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
        int childWindowAppearTimeoutMs = 3000,
        ScreenCapture? capturer = null,
        bool requireSelectionChange = false,
        string? previousSelectionStateSha256 = null,
        int selectionRetryCount = 1
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
        var selectionStateBeforeSha256 = requireSelectionChange && capturer is not null
            ? TryGetListSelectionStateSha256(listWindow, listTarget, capturer)
            : null;

        _controller.Activate(listWindow.Hwnd);
        Thread.Sleep(80);

        string? selectionStateAfterSha256 = null;
        var clickAttempts = 0;
        var maxAttempts = Math.Max(1, selectionRetryCount + 1);
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            clickAttempts++;
            _controller.ClickWindowPoint(listWindow.Hwnd, rowClickPoint);
            Thread.Sleep(Math.Max(0, listSelectionDelayMs));

            selectionStateAfterSha256 = requireSelectionChange && capturer is not null
                ? TryGetListSelectionStateSha256(listWindow, listTarget, capturer)
                : null;

            var selectionMatchesPrevious = requireSelectionChange &&
                                           !string.IsNullOrWhiteSpace(previousSelectionStateSha256) &&
                                           !string.IsNullOrWhiteSpace(selectionStateAfterSha256) &&
                                           string.Equals(previousSelectionStateSha256, selectionStateAfterSha256, StringComparison.OrdinalIgnoreCase);
            if (!selectionMatchesPrevious)
            {
                break;
            }
        }

        if (requireSelectionChange &&
            !string.IsNullOrWhiteSpace(selectionStateBeforeSha256) &&
            !string.IsNullOrWhiteSpace(selectionStateAfterSha256) &&
            string.Equals(selectionStateBeforeSha256, selectionStateAfterSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ListEntrySelectionNotChangedException(
                oneBasedIndex,
                rowClickPoint,
                selectionStateBeforeSha256,
                selectionStateAfterSha256
            );
        }

        if (requireSelectionChange &&
            !string.IsNullOrWhiteSpace(previousSelectionStateSha256) &&
            !string.IsNullOrWhiteSpace(selectionStateAfterSha256) &&
            string.Equals(previousSelectionStateSha256, selectionStateAfterSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ListEntryRepeatedSelectionException(
                oneBasedIndex,
                rowClickPoint,
                previousSelectionStateSha256,
                selectionStateAfterSha256,
                clickAttempts
            );
        }

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
            _controller.Maximize(childWindow.Hwnd);
            Thread.Sleep(120);
            _controller.Activate(childWindow.Hwnd);
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
            ChildWindowRect: childWindow.Rect,
            SelectionStateBeforeSha256: selectionStateBeforeSha256,
            SelectionStateAfterSha256: selectionStateAfterSha256,
            ClickAttempts: clickAttempts
        );
    }

    private static string? TryGetListSelectionStateSha256(
        TestlabWindowInfo listWindow,
        WorkstationListNavigationTarget listTarget,
        ScreenCapture capturer
    )
    {
        if (listTarget.RoiWindow is not BBox roi)
        {
            return null;
        }

        var windowBytes = capturer.CaptureWindowPngBytes(listWindow.Hwnd);
        var roiBytes = ImageCropper.TryCropToPngBytes(windowBytes, roi) ?? windowBytes;
        if (roiBytes.Length == 0)
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(roiBytes)).ToLowerInvariant();
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
    DesktopRect ChildWindowRect,
    string? SelectionStateBeforeSha256 = null,
    string? SelectionStateAfterSha256 = null,
    int ClickAttempts = 1
)
{
    public string ToDebugText()
        => $"list={ListWindowTitle};child={ChildWindowTitle};row={RowIndex};rowPoint=({RowClickPointWindow.X},{RowClickPointWindow.Y});action=({ActionClickPointWindow.X},{ActionClickPointWindow.Y});childRect=({ChildWindowRect.Left},{ChildWindowRect.Top},{ChildWindowRect.Width},{ChildWindowRect.Height});selectionBefore={(SelectionStateBeforeSha256 ?? "<null>")};selectionAfter={(SelectionStateAfterSha256 ?? "<null>")};clickAttempts={ClickAttempts}";
}

public sealed class ListEntryOutOfRangeException : InvalidOperationException
{
    public ListEntryOutOfRangeException(int rowIndex, WindowPoint rowClickPoint, BBox roi)
        : base(
            $"目标序号 [{rowIndex}] 的行点击点超出列表 ROI。point=({rowClickPoint.X},{rowClickPoint.Y}), roi=({roi.X},{roi.Y},{roi.Width},{roi.Height})"
        )
    {
        RowIndex = rowIndex;
        RowClickPoint = rowClickPoint;
        Roi = roi;
    }

    public int RowIndex { get; }
    public WindowPoint RowClickPoint { get; }
    public BBox Roi { get; }
}

public sealed class ListEntrySelectionNotChangedException : InvalidOperationException
{
    public ListEntrySelectionNotChangedException(
        int rowIndex,
        WindowPoint rowClickPoint,
        string? selectionStateBeforeSha256,
        string? selectionStateAfterSha256
    )
        : base(
            $"目标序号 [{rowIndex}] 点击后列表选中态未变化，已阻止继续打开子窗口。point=({rowClickPoint.X},{rowClickPoint.Y}), before={selectionStateBeforeSha256 ?? "<null>"}, after={selectionStateAfterSha256 ?? "<null>"}"
        )
    {
        RowIndex = rowIndex;
        RowClickPoint = rowClickPoint;
        SelectionStateBeforeSha256 = selectionStateBeforeSha256;
        SelectionStateAfterSha256 = selectionStateAfterSha256;
    }

    public int RowIndex { get; }
    public WindowPoint RowClickPoint { get; }
    public string? SelectionStateBeforeSha256 { get; }
    public string? SelectionStateAfterSha256 { get; }
}

public sealed class ListEntryRepeatedSelectionException : InvalidOperationException
{
    public ListEntryRepeatedSelectionException(
        int rowIndex,
        WindowPoint rowClickPoint,
        string? previousSelectionStateSha256,
        string? selectionStateAfterSha256,
        int clickAttempts
    )
        : base(
            $"目标序号 [{rowIndex}] 点击后仍停留在上一条有效选中态，已阻止继续打开子窗口。point=({rowClickPoint.X},{rowClickPoint.Y}), previous={previousSelectionStateSha256 ?? "<null>"}, current={selectionStateAfterSha256 ?? "<null>"}, clickAttempts={clickAttempts}"
        )
    {
        RowIndex = rowIndex;
        RowClickPoint = rowClickPoint;
        PreviousSelectionStateSha256 = previousSelectionStateSha256;
        SelectionStateAfterSha256 = selectionStateAfterSha256;
        ClickAttempts = clickAttempts;
    }

    public int RowIndex { get; }
    public WindowPoint RowClickPoint { get; }
    public string? PreviousSelectionStateSha256 { get; }
    public string? SelectionStateAfterSha256 { get; }
    public int ClickAttempts { get; }
}
