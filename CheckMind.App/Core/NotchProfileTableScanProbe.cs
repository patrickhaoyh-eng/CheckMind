using System.IO;
using System.Text;

namespace CheckMind.App.Core;

public sealed class NotchProfileTableScanProbe
{
    public NotchProfileTableScanProbeResult Run(RunContext run)
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

        var notchWindowPngBytes = capturer.CaptureWindowPngBytes(notchWindow.Hwnd);
        var tableEntryBytes = ImageCropper.TryCropToPngBytes(notchWindowPngBytes, tableRoiWindow) ?? notchWindowPngBytes;
        var tableEntryPath = screenshotStore.SaveEvidencePng(run, "probe_notch_profile_table_entry", tableEntryBytes);
        var tableRoiScreen = MapWindowRoiToScreenRoi(tableRoiWindow, notchWindow);

        var entry = new TestlabTableEntryResult(
            "Notch Profile",
            "Notch Profile Table",
            tableEntryPath,
            tableRoiWindow,
            tableRoiScreen,
            tableScanTarget.PagingFocusPointWindow,
            tableScanTarget.PagingActivationPointWindow,
            tableScanTarget.PagingPreparationMode
        );
        var fixedProfile = WorkstationProfileStore.CreateDefault().Load();
        var maxSteps = GetIntEnv("CHECKMIND_TABLE_SCAN_V_MAX_STEPS", 8, 1, 50);
        var pauseMs = GetIntEnv("CHECKMIND_TABLE_SCAN_PAUSE_MS", 250, 80, 2000);
        var tableScan = TestlabAutomationRunner.ScanSingleTableWithDeterministicPaging(
            run,
            controller,
            overlay: null,
            capturer,
            screenshotStore,
            notchWindow,
            entry,
            fixedProfile,
            childWindowProfile: null,
            maxSteps,
            pauseMs
        );

        var (closeMode, childWindowClosed) = CloseChildWindow(
            controller,
            childLocator,
            notchWindow,
            childTitleContains,
            defineWindow.ProcessName,
            notchProfileWindow
        );

        string? defineWindowAfterCloseScreenshotPath = null;
        if (childWindowClosed)
        {
            var returnedWindow = new TestlabWindowLocator().Find();
            controller.Activate(returnedWindow.Hwnd);
            Thread.Sleep(120);
            defineWindowAfterCloseScreenshotPath = screenshotStore.SaveEvidencePng(
                run,
                "probe_define_notch_profiles_window_after_table_scan",
                capturer.CaptureWindowPngBytes(returnedWindow.Hwnd)
            );
        }

        var result = new NotchProfileTableScanProbeResult(
            TargetRowIndex: rowIndex,
            Opened: opened,
            ProfilePath: profileStore.ProfilePath,
            TableEntryScreenshotPath: tableEntryPath,
            TableRoiWindow: tableRoiWindow,
            TableRoiScreen: tableRoiScreen,
            ChunkCount: tableScan.Chunks.Count,
            UniqueChunkCount: tableScan.UniqueChunkCount,
            StitchedScreenshotPath: tableScan.StitchedScreenshotPath,
            CloseMode: closeMode,
            ChildWindowClosed: childWindowClosed,
            DefineNotchProfilesAfterCloseScreenshotPath: defineWindowAfterCloseScreenshotPath
        );

        var resultPath = Path.Combine(run.RunDirectory, "notch_profile_table_scan_probe.json");
        File.WriteAllText(resultPath, result.ToJson(), new UTF8Encoding(false));
        TestlabDebugMarkers.WritePhase(
            "probe.notch_profile_table_scan.completed",
            run.RunDirectory,
            $"row={rowIndex};chunks={result.ChunkCount};unique={result.UniqueChunkCount};closed={(childWindowClosed ? 1 : 0)}"
        );
        return result;
    }

    private static BBox MapWindowRoiToScreenRoi(BBox roiWindow, TestlabWindowInfo win)
    {
        var screenX = win.Rect.Left + Math.Clamp(roiWindow.X, 0, Math.Max(0, win.Rect.Width - 1));
        var screenY = win.Rect.Top + Math.Clamp(roiWindow.Y, 0, Math.Max(0, win.Rect.Height - 1));
        var maxWidth = Math.Max(1, win.Rect.Width - (screenX - win.Rect.Left));
        var maxHeight = Math.Max(1, win.Rect.Height - (screenY - win.Rect.Top));

        return new BBox(
            screenX,
            screenY,
            Math.Clamp(roiWindow.Width, 1, maxWidth),
            Math.Clamp(roiWindow.Height, 1, maxHeight)
        );
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
}

public sealed record NotchProfileTableScanProbeResult(
    int TargetRowIndex,
    TestlabChildWindowOpenResult Opened,
    string ProfilePath,
    string TableEntryScreenshotPath,
    BBox TableRoiWindow,
    BBox TableRoiScreen,
    int ChunkCount,
    int UniqueChunkCount,
    string? StitchedScreenshotPath,
    string CloseMode,
    bool ChildWindowClosed,
    string? DefineNotchProfilesAfterCloseScreenshotPath
)
{
    public string ToJson()
        => System.Text.Json.JsonSerializer.Serialize(this, JsonOptions.Default);
}
