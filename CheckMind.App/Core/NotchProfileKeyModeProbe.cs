using System.IO;
using System.Text;
using System.Windows;

namespace CheckMind.App.Core;

public sealed class NotchProfileKeyModeProbe
{
    public NotchProfileKeyModeProbeResult Run(RunContext run)
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

        var waitSeconds = GetIntEnv("CHECKMIND_NOTCH_KEY_PROBE_WAIT_SECONDS", 8, 2, 30);
        var keyDelayMs = GetIntEnv("CHECKMIND_TABLE_KEY_DELAY_MS", 10, 0, 1000);
        var pagePauseMs = GetIntEnv("CHECKMIND_TABLE_PAGE_PAUSE_MS", 25, 0, 2000);
        var resetTopBeforePgdn = GetBoolEnv("CHECKMIND_NOTCH_KEY_PROBE_RESET_TOP", defaultValue: false);
        var resetTopPgupCount = GetIntEnv("CHECKMIND_TABLE_RESET_TOP_PGUP_COUNT", 10, 1, 30);
        var mode = GetProbeMode();
        var autoClickBeforeDispatch = GetBoolEnv("CHECKMIND_NOTCH_KEY_PROBE_AUTO_CLICK", defaultValue: false);
        var autoClickMode = GetProbeClickMode();
        var autoClickCount = GetIntEnv("CHECKMIND_NOTCH_KEY_PROBE_CLICK_COUNT", 1, 1, 5);
        var autoClickPauseMs = GetIntEnv("CHECKMIND_NOTCH_KEY_PROBE_CLICK_PAUSE_MS", 120, 0, 2000);
        var clickPointWindow = tableScanTarget.PagingActivationPointWindow
            ?? tableScanTarget.PagingFocusPointWindow
            ?? new WindowPoint(
                tableRoiWindow.X + Math.Max(0, tableRoiWindow.Width / 2),
                tableRoiWindow.Y + Math.Max(0, Math.Min(tableRoiWindow.Height - 1, 180))
            );

        System.Windows.MessageBox.Show(
            "\u7a0b\u5e8f\u5c06\u81ea\u52a8\u6253\u5f00 Notch Profile\uff0c\u8bf7\u5728\u5012\u8ba1\u65f6\u5185\u6309\u63d0\u793a\u914d\u5408\u3002" + Environment.NewLine +
            (autoClickBeforeDispatch
                ? $"\u672c\u8f6e\u7531\u7a0b\u5e8f\u81ea\u52a8\u70b9\u51fb\u6d4b\u8bd5\u70b9 ({clickPointWindow.X},{clickPointWindow.Y})\uff0c\u70b9\u51fb\u6a21\u5f0f={autoClickMode}\uff0c\u70b9\u51fb\u6b21\u6570={autoClickCount}\u3002" + Environment.NewLine +
                  "\u4f60\u53ea\u9700\u8981\u70b9 OK \u540e\u4e0d\u8981\u518d\u78b0\u9f20\u6807\u6216\u952e\u76d8\u3002" + Environment.NewLine
                : "\u8bf7\u5148\u7528\u9f20\u6807\u70b9\u51fb\u4f60\u73b0\u573a\u9a8c\u8bc1\u8fc7\u7684\u771f\u5b9e\u53ef\u7ffb\u9875\u4f4d\u7f6e\uff0c\u7136\u540e\u4fdd\u6301\u9f20\u6807\u4e0d\u52a8\uff0c\u4e0d\u8981\u81ea\u5df1\u6309 PgDn\u3002" + Environment.NewLine) +
            $"\u7a0b\u5e8f\u4f1a\u5728 {waitSeconds} \u79d2\u540e\u81ea\u52a8\u7528 {mode} \u53d1\u952e\u3002" + Environment.NewLine +
            (resetTopBeforePgdn
                ? $"\u672c\u8f6e\u4f1a\u5148\u81ea\u52a8 PgUp x{resetTopPgupCount}\uff0c\u518d\u81ea\u52a8 PgDn \u4e00\u6b21\u3002"
                : "\u672c\u8f6e\u53ea\u4f1a\u81ea\u52a8 PgDn \u4e00\u6b21\u3002"),
            "CheckMind - Notch Profile \u53d1\u952e\u8def\u5f84 Probe",
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

        var beforeWindowBytes = capturer.CaptureWindowPngBytes(notchWindow.Hwnd);
        var modeTag = NormalizeMode(mode);
        var beforeWindowPath = screenshotStore.SaveDebugPng(run, $"probe_notch_profile_keymode_{modeTag}_window_before", beforeWindowBytes);
        var beforeTableBytes = ImageCropper.TryCropToPngBytes(beforeWindowBytes, tableRoiWindow) ?? beforeWindowBytes;
        var beforeTablePath = screenshotStore.SaveEvidencePng(run, $"probe_notch_profile_keymode_{modeTag}_table_before", beforeTableBytes);

        var serialRoiWindow = BuildSerialRoiWindow(tableRoiWindow);
        var scrollbarRoiWindow = BuildScrollbarRoiWindow(tableRoiWindow);
        var beforeSerialBytes = ImageCropper.TryCropToPngBytes(beforeWindowBytes, serialRoiWindow) ?? beforeTableBytes;
        var beforeScrollbarBytes = ImageCropper.TryCropToPngBytes(beforeWindowBytes, scrollbarRoiWindow) ?? beforeTableBytes;
        var beforeSerialSha = ComputeSha256Hex(beforeSerialBytes);
        var beforeScrollbarSha = ComputeSha256Hex(beforeScrollbarBytes);

        TestlabDebugMarkers.WritePhase(
            "probe.notch_profile_key_mode.before",
            run.RunDirectory,
            $"row={rowIndex};mode={mode};waitSeconds={waitSeconds};resetTop={(resetTopBeforePgdn ? 1 : 0)};resetTopPgupCount={resetTopPgupCount};autoClick={(autoClickBeforeDispatch ? 1 : 0)};autoClickMode={autoClickMode};autoClickCount={autoClickCount};autoClickPauseMs={autoClickPauseMs};clickPointWindow=({clickPointWindow.X},{clickPointWindow.Y});beforeSerial={beforeSerialSha};beforeScrollbar={beforeScrollbarSha};windowPath={beforeWindowPath};tablePath={beforeTablePath}"
        );

        Thread.Sleep(waitSeconds * 1000);

        if (autoClickBeforeDispatch)
        {
            var clickScreenX = notchWindow.Rect.Left + clickPointWindow.X;
            var clickScreenY = notchWindow.Rect.Top + clickPointWindow.Y;
            TestlabDebugMarkers.WritePhase(
                "probe.notch_profile_key_mode.auto_click",
                run.RunDirectory,
                $"row={rowIndex};mode={mode};clickMode={autoClickMode};clickCount={autoClickCount};clickPauseMs={autoClickPauseMs};clickPointWindow=({clickPointWindow.X},{clickPointWindow.Y});clickPointScreen=({clickScreenX},{clickScreenY})"
            );
            DispatchPointerClicks(controller, clickScreenX, clickScreenY, autoClickMode, autoClickCount, autoClickPauseMs);
            Thread.Sleep(autoClickPauseMs);
        }

        var cursor = controller.GetCursorScreenPoint();
        var cursorX = cursor?.X ?? -1;
        var cursorY = cursor?.Y ?? -1;
        var pointWindow = cursor is DesktopPoint p ? controller.GetWindowFromScreenPoint(p.X, p.Y) : IntPtr.Zero;
        var pointWindowTitle = Win32Native.GetWindowTitle(pointWindow);
        var foregroundBefore = controller.GetForegroundWindowHandle();
        var foregroundBeforeTitle = Win32Native.GetWindowTitle(foregroundBefore);

        TestlabDebugMarkers.WritePhase(
            "probe.notch_profile_key_mode.dispatch",
            run.RunDirectory,
            $"row={rowIndex};mode={mode};resetTop={(resetTopBeforePgdn ? 1 : 0)};resetTopPgupCount={resetTopPgupCount};autoClick={(autoClickBeforeDispatch ? 1 : 0)};autoClickMode={autoClickMode};cursor=({cursorX},{cursorY});pointWindow=0x{pointWindow.ToInt64():X};pointWindowTitle={Sanitize(pointWindowTitle)};foregroundBefore=0x{foregroundBefore.ToInt64():X};foregroundBeforeTitle={Sanitize(foregroundBeforeTitle)};notchHwnd=0x{notchWindow.Hwnd.ToInt64():X}"
        );

        if (resetTopBeforePgdn)
        {
            for (var i = 0; i < resetTopPgupCount; i++)
            {
                DispatchPgUp(controller, mode, pointWindow, notchWindow.Hwnd, keyDelayMs);
                Thread.Sleep(pagePauseMs);
            }
        }

        DispatchKey(controller, mode, pointWindow, notchWindow.Hwnd, keyDelayMs);
        Thread.Sleep(pagePauseMs);

        var foregroundAfter = controller.GetForegroundWindowHandle();
        var foregroundAfterTitle = Win32Native.GetWindowTitle(foregroundAfter);
        TestlabDebugMarkers.WritePhase(
            "probe.notch_profile_key_mode.after_dispatch",
            run.RunDirectory,
            $"row={rowIndex};mode={mode};foregroundAfter=0x{foregroundAfter.ToInt64():X};foregroundAfterTitle={Sanitize(foregroundAfterTitle)}"
        );

        var afterWindowBytes = capturer.CaptureWindowPngBytes(notchWindow.Hwnd);
        var afterWindowPath = screenshotStore.SaveDebugPng(run, $"probe_notch_profile_keymode_{modeTag}_window_after", afterWindowBytes);
        var afterTableBytes = ImageCropper.TryCropToPngBytes(afterWindowBytes, tableRoiWindow) ?? afterWindowBytes;
        var afterTablePath = screenshotStore.SaveEvidencePng(run, $"probe_notch_profile_keymode_{modeTag}_table_after", afterTableBytes);
        var afterSerialBytes = ImageCropper.TryCropToPngBytes(afterWindowBytes, serialRoiWindow) ?? afterTableBytes;
        var afterScrollbarBytes = ImageCropper.TryCropToPngBytes(afterWindowBytes, scrollbarRoiWindow) ?? afterTableBytes;
        var afterSerialSha = ComputeSha256Hex(afterSerialBytes);
        var afterScrollbarSha = ComputeSha256Hex(afterScrollbarBytes);
        var serialChanged = !string.Equals(beforeSerialSha, afterSerialSha, StringComparison.OrdinalIgnoreCase);
        var scrollbarChanged = !string.Equals(beforeScrollbarSha, afterScrollbarSha, StringComparison.OrdinalIgnoreCase);

        TestlabDebugMarkers.WritePhase(
            "probe.notch_profile_key_mode.after",
            run.RunDirectory,
            $"row={rowIndex};mode={mode};afterSerial={afterSerialSha};afterScrollbar={afterScrollbarSha};serialChanged={(serialChanged ? 1 : 0)};scrollbarChanged={(scrollbarChanged ? 1 : 0)};windowPath={afterWindowPath};tablePath={afterTablePath}"
        );

        var (closeMode, childWindowClosed) = CloseChildWindow(
            controller,
            childLocator,
            notchWindow,
            childTitleContains,
            defineWindow.ProcessName,
            notchProfileWindow
        );

        var result = new NotchProfileKeyModeProbeResult(
            TargetRowIndex: rowIndex,
            Mode: mode,
            AutoClickBeforeDispatch: autoClickBeforeDispatch,
            AutoClickMode: autoClickMode,
            AutoClickCount: autoClickCount,
            AutoClickPauseMs: autoClickPauseMs,
            ClickPointWindow: clickPointWindow,
            Opened: opened,
            ProfilePath: profileStore.ProfilePath,
            CursorBeforeDispatch: cursor,
            PointWindowHandleBeforeDispatchHex: $"0x{pointWindow.ToInt64():X}",
            PointWindowTitleBeforeDispatch: pointWindowTitle,
            ForegroundHandleBeforeDispatchHex: $"0x{foregroundBefore.ToInt64():X}",
            ForegroundTitleBeforeDispatch: foregroundBeforeTitle,
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

        var resultPath = Path.Combine(run.RunDirectory, "notch_profile_key_mode_probe.json");
        File.WriteAllText(resultPath, result.ToJson(), new UTF8Encoding(false));
        TestlabDebugMarkers.WritePhase(
            "probe.notch_profile_key_mode.completed",
            run.RunDirectory,
            $"row={rowIndex};mode={mode};serialChanged={(serialChanged ? 1 : 0)};scrollbarChanged={(scrollbarChanged ? 1 : 0)};closed={(childWindowClosed ? 1 : 0)}"
        );
        return result;
    }

    private static void DispatchKey(
        WindowController controller,
        string mode,
        IntPtr pointWindow,
        IntPtr notchWindow,
        int keyDelayMs
    )
    {
        if (string.Equals(mode, "window_message", StringComparison.OrdinalIgnoreCase))
        {
            controller.PressPageDownToWindow(pointWindow != IntPtr.Zero ? pointWindow : notchWindow, keyDelayMs);
            return;
        }

        if (string.Equals(mode, "sendinput_foreground", StringComparison.OrdinalIgnoreCase))
        {
            controller.PressPageDownToForegroundWindowBySendInput(keyDelayMs);
            return;
        }

        controller.PressPageDownToForegroundWindow(keyDelayMs);
    }

    private static void DispatchPointerClicks(
        WindowController controller,
        int x,
        int y,
        string clickMode,
        int clickCount,
        int clickPauseMs
    )
    {
        var normalizedClickCount = Math.Max(1, clickCount);
        for (var i = 0; i < normalizedClickCount; i++)
        {
            if (string.Equals(clickMode, "sendinput", StringComparison.OrdinalIgnoreCase))
            {
                controller.ClickScreenPointBySendInput(x, y);
            }
            else
            {
                controller.ClickScreenPoint(x, y);
            }

            Thread.Sleep(Math.Max(0, clickPauseMs));
        }
    }

    private static void DispatchPgUp(
        WindowController controller,
        string mode,
        IntPtr pointWindow,
        IntPtr notchWindow,
        int keyDelayMs
    )
    {
        if (string.Equals(mode, "window_message", StringComparison.OrdinalIgnoreCase))
        {
            controller.PressPageUpToWindow(pointWindow != IntPtr.Zero ? pointWindow : notchWindow, keyDelayMs);
            return;
        }

        if (string.Equals(mode, "sendinput_foreground", StringComparison.OrdinalIgnoreCase))
        {
            controller.PressPageUpToForegroundWindowBySendInput(keyDelayMs);
            return;
        }

        controller.PressPageUpToForegroundWindow(keyDelayMs);
    }

    private static string GetProbeMode()
    {
        var raw = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_KEY_PROBE_MODE") ?? string.Empty).Trim();
        if (string.Equals(raw, "window_message", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "message", StringComparison.OrdinalIgnoreCase))
        {
            return "window_message";
        }

        if (string.Equals(raw, "sendinput_foreground", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "sendinput", StringComparison.OrdinalIgnoreCase))
        {
            return "sendinput_foreground";
        }

        return "foreground";
    }

    private static string GetProbeClickMode()
    {
        var raw = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_KEY_PROBE_CLICK_MODE") ?? string.Empty).Trim();
        if (string.Equals(raw, "sendinput", StringComparison.OrdinalIgnoreCase))
        {
            return "sendinput";
        }

        return "mouse_event";
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

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        return value
            .Replace(";", ",", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string NormalizeMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "default";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.Length == 0 ? "default" : builder.ToString();
    }

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
}

public sealed record NotchProfileKeyModeProbeResult(
    int TargetRowIndex,
    string Mode,
    bool AutoClickBeforeDispatch,
    string AutoClickMode,
    int AutoClickCount,
    int AutoClickPauseMs,
    WindowPoint ClickPointWindow,
    TestlabChildWindowOpenResult Opened,
    string ProfilePath,
    DesktopPoint? CursorBeforeDispatch,
    string PointWindowHandleBeforeDispatchHex,
    string PointWindowTitleBeforeDispatch,
    string ForegroundHandleBeforeDispatchHex,
    string ForegroundTitleBeforeDispatch,
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
