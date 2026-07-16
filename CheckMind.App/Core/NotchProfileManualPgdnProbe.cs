using System.IO;
using System.Text;
using System.Windows;

namespace CheckMind.App.Core;

public sealed class NotchProfileManualPgdnProbe
{
    public NotchProfileManualPgdnProbeResult Run(RunContext run)
    {
        var profileStore = WorkstationProfileStore.CreateDefault();
        var profile = profileStore.Load();
        var screenshotStore = new ScreenshotStore();
        var capturer = new ScreenCapture();
        var controller = new WindowController();
        var childLocator = new TestlabChildWindowLocator();
        var automation = new TestlabChildWindowAutomation(childLocator, controller);

        var mainWindow = new TestlabWindowLocator().Find();
        var defineNotchWindow = profile.FindChildWindowProfile("define_notch_profiles")
            ?? throw new InvalidOperationException("Profile missing ChildWindows.define_notch_profiles.");
        var listTarget = defineNotchWindow.FindListTarget("notch_profiles_list")
            ?? throw new InvalidOperationException("Profile missing define_notch_profiles.listTargets.notch_profiles_list.");
        var notchProfileWindow = profile.FindChildWindowProfile("notch_profile")
            ?? throw new InvalidOperationException("Profile missing ChildWindows.notch_profile.");
        var tableScanTarget = notchProfileWindow.FindCaptureTarget("table_scan")
            ?? throw new InvalidOperationException("Profile missing notch_profile.captureTargets.table_scan.");
        if (tableScanTarget.RoiWindow is not BBox tableRoiWindow)
        {
            throw new InvalidOperationException("Profile missing notch_profile.captureTargets.table_scan.RoiWindow.");
        }
        var pagingFocusPointWindow = tableScanTarget.PagingFocusPointWindow;

        var manualWaitSeconds = GetIntEnv("CHECKMIND_NOTCH_MANUAL_PGDN_WAIT_SECONDS", 8, 2, 30);
        var autoFocusBeforeWait = GetBoolEnv("CHECKMIND_NOTCH_MANUAL_AUTO_FOCUS", defaultValue: false);

        System.Windows.MessageBox.Show(
            "\u7a0b\u5e8f\u5c06\u81ea\u52a8\u6253\u5f00 Notch Profile\uff0c\u4f46\u4e0d\u4f1a\u4ee3\u66ff\u70b9\u51fb\u8868\u683c\u7126\u70b9\u3002" + Environment.NewLine +
            $"\u8bf7\u5728 {manualWaitSeconds} \u79d2\u5185\u5148\u7528\u9f20\u6807\u70b9\u51fb\u4f60\u73b0\u573a\u9a8c\u8bc1\u8fc7\u7684\u771f\u5b9e\u53ef\u7ffb\u9875\u4f4d\u7f6e\uff0c\u518d\u624b\u52a8\u6309\u4e00\u6b21 PgDn\u3002" + Environment.NewLine +
            "\u65f6\u95f4\u5230\u540e\u7a0b\u5e8f\u4f1a\u81ea\u52a8\u6293\u53d6\u540e\u72b6\u6001\u5e76\u751f\u6210\u8bc1\u636e\u3002",
            "CheckMind - Notch Profile \u4eba\u5de5 PgDn \u5bf9\u7167",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information
        );

        var rowIndex = GetIntEnv("CHECKMIND_NOTCH_PROFILE_INDEX", 1, 1, 9999);
        var childTitleContains = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_PROFILE_TITLE_CONTAINS") ?? "Notch Profile").Trim();
        if (string.IsNullOrWhiteSpace(childTitleContains))
        {
            childTitleContains = "Notch Profile";
        }

        var entryClickSequence = defineNotchWindow.GetOpenClickSequence();
        if (entryClickSequence.Length == 0)
        {
            throw new InvalidOperationException("Profile missing define_notch_profiles.openClickSequence / openClickPoint.");
        }

        controller.Activate(mainWindow.Hwnd);
        Thread.Sleep(80);
        foreach (var point in entryClickSequence)
        {
            controller.ClickWindowPoint(mainWindow.Hwnd, point);
            Thread.Sleep(180);
        }
        Thread.Sleep(600);

        var defineWindow = new TestlabWindowLocator().Find();
        controller.Maximize(defineWindow.Hwnd);
        Thread.Sleep(250);
        controller.Activate(defineWindow.Hwnd);
        Thread.Sleep(80);

        var opened = automation.OpenChildWindowFromIndexedListEntry(
            defineWindow,
            listTarget,
            rowIndex,
            childTitleContains,
            maximizeChildWindow: true
        );

        var notchWindow = childLocator.FindByTitleContains(
            childTitleContains,
            processName: defineWindow.ProcessName,
            timeoutMs: 1000
        );
        controller.Activate(notchWindow.Hwnd);
        Thread.Sleep(120);

        if (autoFocusBeforeWait && pagingFocusPointWindow is WindowPoint focusPoint)
        {
            var focusScreenX = notchWindow.Rect.Left + focusPoint.X;
            var focusScreenY = notchWindow.Rect.Top + focusPoint.Y;
            FocusTableForManualPaging(controller, notchWindow, focusScreenX, focusScreenY);
        }

        var beforeWindowBytes = capturer.CaptureWindowPngBytes(notchWindow.Hwnd);
        var beforeWindowPath = screenshotStore.SaveDebugPng(run, "probe_notch_profile_manual_pgdn_window_before", beforeWindowBytes);
        var beforeTableBytes = ImageCropper.TryCropToPngBytes(beforeWindowBytes, tableRoiWindow) ?? beforeWindowBytes;
        var beforeTablePath = screenshotStore.SaveEvidencePng(run, "probe_notch_profile_manual_pgdn_table_before", beforeTableBytes);

        var serialRoiWindow = BuildSerialRoiWindow(tableRoiWindow);
        var scrollbarRoiWindow = BuildScrollbarRoiWindow(tableRoiWindow);
        var beforeSerialBytes = ImageCropper.TryCropToPngBytes(beforeWindowBytes, serialRoiWindow) ?? beforeTableBytes;
        var beforeScrollbarBytes = ImageCropper.TryCropToPngBytes(beforeWindowBytes, scrollbarRoiWindow) ?? beforeTableBytes;
        var beforeSerialSha = ComputeSha256Hex(beforeSerialBytes);
        var beforeScrollbarSha = ComputeSha256Hex(beforeScrollbarBytes);

        TestlabDebugMarkers.WritePhase(
            "probe.notch_profile_manual_pgdn.before",
            run.RunDirectory,
            $"row={rowIndex};focusWindow={FormatFocusPoint(pagingFocusPointWindow)};autoFocus={(autoFocusBeforeWait ? 1 : 0)};waitSeconds={manualWaitSeconds};beforeSerial={beforeSerialSha};beforeScrollbar={beforeScrollbarSha};windowPath={beforeWindowPath};tablePath={beforeTablePath}"
        );
        Thread.Sleep(manualWaitSeconds * 1000);

        var afterWindowBytes = capturer.CaptureWindowPngBytes(notchWindow.Hwnd);
        var afterWindowPath = screenshotStore.SaveDebugPng(run, "probe_notch_profile_manual_pgdn_window_after", afterWindowBytes);
        var afterTableBytes = ImageCropper.TryCropToPngBytes(afterWindowBytes, tableRoiWindow) ?? afterWindowBytes;
        var afterTablePath = screenshotStore.SaveEvidencePng(run, "probe_notch_profile_manual_pgdn_table_after", afterTableBytes);
        var afterSerialBytes = ImageCropper.TryCropToPngBytes(afterWindowBytes, serialRoiWindow) ?? afterTableBytes;
        var afterScrollbarBytes = ImageCropper.TryCropToPngBytes(afterWindowBytes, scrollbarRoiWindow) ?? afterTableBytes;
        var afterSerialSha = ComputeSha256Hex(afterSerialBytes);
        var afterScrollbarSha = ComputeSha256Hex(afterScrollbarBytes);

        var serialChanged = !string.Equals(beforeSerialSha, afterSerialSha, StringComparison.OrdinalIgnoreCase);
        var scrollbarChanged = !string.Equals(beforeScrollbarSha, afterScrollbarSha, StringComparison.OrdinalIgnoreCase);

        TestlabDebugMarkers.WritePhase(
            "probe.notch_profile_manual_pgdn.after",
            run.RunDirectory,
            $"row={rowIndex};focusWindow={FormatFocusPoint(pagingFocusPointWindow)};autoFocus={(autoFocusBeforeWait ? 1 : 0)};afterSerial={afterSerialSha};afterScrollbar={afterScrollbarSha};serialChanged={(serialChanged ? 1 : 0)};scrollbarChanged={(scrollbarChanged ? 1 : 0)};windowPath={afterWindowPath};tablePath={afterTablePath}"
        );

        var (closeMode, childWindowClosed) = CloseChildWindow(
            controller,
            childLocator,
            notchWindow,
            childTitleContains,
            defineWindow.ProcessName,
            notchProfileWindow
        );

        var result = new NotchProfileManualPgdnProbeResult(
            TargetRowIndex: rowIndex,
            Opened: opened,
            ProfilePath: profileStore.ProfilePath,
            FocusPointWindow: pagingFocusPointWindow,
            BeforeWindowScreenshotPath: beforeWindowPath,
            BeforeTableScreenshotPath: beforeTablePath,
            AfterWindowScreenshotPath: afterWindowPath,
            AfterTableScreenshotPath: afterTablePath,
            BeforeSerialSha256: beforeSerialSha,
            AfterSerialSha256: afterSerialSha,
            BeforeScrollbarSha256: beforeScrollbarSha,
            AfterScrollbarSha256: afterScrollbarSha,
            SerialChanged: serialChanged,
            ScrollbarChanged: scrollbarChanged,
            CloseMode: closeMode,
            ChildWindowClosed: childWindowClosed
        );

        var resultPath = Path.Combine(run.RunDirectory, "notch_profile_manual_pgdn_probe.json");
        File.WriteAllText(resultPath, result.ToJson(), new UTF8Encoding(false));
        TestlabDebugMarkers.WritePhase(
            "probe.notch_profile_manual_pgdn.completed",
            run.RunDirectory,
            $"row={rowIndex};serialChanged={(serialChanged ? 1 : 0)};scrollbarChanged={(scrollbarChanged ? 1 : 0)};closed={(childWindowClosed ? 1 : 0)}"
        );
        return result;
    }

    private static void FocusTableForManualPaging(WindowController controller, TestlabWindowInfo notchWindow, int focusScreenX, int focusScreenY)
    {
        var focusClickCount = GetIntEnv("CHECKMIND_TABLE_FOCUS_CLICK_COUNT", 2, 1, 5);
        var focusClickPauseMs = GetIntEnv("CHECKMIND_TABLE_FOCUS_CLICK_PAUSE_MS", 90, 0, 1000);
        var focusSettleMs = GetIntEnv("CHECKMIND_TABLE_FOCUS_SETTLE_MS", 220, 0, 2000);

        controller.Activate(notchWindow.Hwnd);
        Thread.Sleep(focusClickPauseMs);
        for (var i = 0; i < focusClickCount; i++)
        {
            controller.ClickScreenPoint(focusScreenX, focusScreenY);
            Thread.Sleep(focusClickPauseMs);
        }
        Thread.Sleep(focusSettleMs);
    }

    private static BBox BuildSerialRoiWindow(BBox tableRoiWindow)
    {
        var serialWidth = Math.Clamp((int)Math.Round(tableRoiWindow.Width * 0.12), 80, 280);
        serialWidth = Math.Clamp(serialWidth, 20, Math.Max(20, tableRoiWindow.Width));
        return new BBox(tableRoiWindow.X, tableRoiWindow.Y, serialWidth, tableRoiWindow.Height);
    }

    private static BBox BuildScrollbarRoiWindow(BBox tableRoiWindow)
    {
        var width = Math.Min(18, tableRoiWindow.Width);
        return new BBox(
            tableRoiWindow.X + Math.Max(0, tableRoiWindow.Width - width),
            tableRoiWindow.Y,
            width,
            tableRoiWindow.Height
        );
    }

    private static string ComputeSha256Hex(byte[] bytes)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    private static (string CloseMode, bool Closed) CloseChildWindow(
        WindowController controller,
        TestlabChildWindowLocator childLocator,
        TestlabWindowInfo openedChildWindow,
        string childTitleContains,
        string? processName,
        WorkstationChildWindowProfile childWindowProfile
    )
    {
        controller.Activate(openedChildWindow.Hwnd);
        Thread.Sleep(120);

        var closeMode = "alt_f4";
        if (childWindowProfile.CloseClickPoint is WindowPoint closeClickPoint)
        {
            controller.ClickWindowPoint(openedChildWindow.Hwnd, closeClickPoint);
            closeMode = "click_point";
        }
        else
        {
            controller.CloseForegroundWindowByShortcut();
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            Thread.Sleep(120);
            if (childLocator.TryFindByTitleContains(childTitleContains, processName) is null)
            {
                return (closeMode, true);
            }
        }

        return (closeMode, false);
    }

    private static int GetIntEnv(string key, int defaultValue, int minValue, int maxValue)
    {
        var raw = (Environment.GetEnvironmentVariable(key) ?? string.Empty).Trim();
        var value = int.TryParse(raw, out var parsed) ? parsed : defaultValue;
        return Math.Clamp(value, minValue, maxValue);
    }

    private static bool GetBoolEnv(string key, bool defaultValue)
    {
        var raw = (Environment.GetEnvironmentVariable(key) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatFocusPoint(WindowPoint? point)
        => point is WindowPoint p ? $"({p.X},{p.Y})" : "<none>";

}

public sealed record NotchProfileManualPgdnProbeResult(
    int TargetRowIndex,
    TestlabChildWindowOpenResult Opened,
    string ProfilePath,
    WindowPoint? FocusPointWindow,
    string BeforeWindowScreenshotPath,
    string BeforeTableScreenshotPath,
    string AfterWindowScreenshotPath,
    string AfterTableScreenshotPath,
    string BeforeSerialSha256,
    string AfterSerialSha256,
    string BeforeScrollbarSha256,
    string AfterScrollbarSha256,
    bool SerialChanged,
    bool ScrollbarChanged,
    string CloseMode,
    bool ChildWindowClosed
)
{
    public string ToJson()
        => System.Text.Json.JsonSerializer.Serialize(this, JsonOptions.Default);
}
