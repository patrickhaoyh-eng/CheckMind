using System.IO;
using System.Text;

namespace CheckMind.App.Core;

public sealed class NotchProfilesEntryProbe
{
    public NotchProfilesEntryProbeResult Run(RunContext run)
    {
        var profileStore = WorkstationProfileStore.CreateDefault();
        var profile = profileStore.Load();
        var screenshotStore = new ScreenshotStore();
        var capturer = new ScreenCapture();

        var mainWindow = new TestlabWindowLocator().Find();
        var childLocator = new TestlabChildWindowLocator();
        var controller = new WindowController();
        var automation = new TestlabChildWindowAutomation(childLocator, controller);

        var defineNotchWindow = profile.FindChildWindowProfile("define_notch_profiles")
            ?? throw new InvalidOperationException("profile 缺少 ChildWindows.define_notch_profiles 配置。");
        var listTarget = defineNotchWindow.FindListTarget("notch_profiles_list")
            ?? throw new InvalidOperationException("profile 缺少 define_notch_profiles.listTargets.notch_profiles_list 配置。");
        var childWindowKey = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_PROFILE_WINDOW_KEY") ?? "notch_profile").Trim();
        if (string.IsNullOrWhiteSpace(childWindowKey))
        {
            childWindowKey = "notch_profile";
        }
        var childWindowProfile = profile.FindChildWindowProfile(childWindowKey);

        var rowIndexRaw = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_PROFILE_INDEX") ?? "1").Trim();
        if (!int.TryParse(rowIndexRaw, out var rowIndex) || rowIndex <= 0)
        {
            throw new InvalidOperationException($"CHECKMIND_NOTCH_PROFILE_INDEX 非法：{rowIndexRaw}");
        }

        var entryClickSequence = defineNotchWindow.GetOpenClickSequence();
        if (entryClickSequence.Length == 0)
        {
            throw new InvalidOperationException("profile 缺少 define_notch_profiles.openClickSequence / openClickPoint 配置。");
        }
        var childTitleContains = Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_PROFILE_TITLE_CONTAINS");
        if (string.IsNullOrWhiteSpace(childTitleContains))
        {
            childTitleContains = "Notch Profile";
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
        var defineWindowScreenshotPath = screenshotStore.SaveEvidencePng(
            run,
            "probe_define_notch_profiles_window",
            capturer.CaptureWindowPngBytes(defineWindow.Hwnd)
        );

        var opened = automation.OpenChildWindowFromIndexedListEntry(
            defineWindow,
            listTarget,
            rowIndex,
            childTitleContains,
            maximizeChildWindow: true
        );
        var openedChildWindow = childLocator.FindByTitleContains(
            childTitleContains,
            processName: defineWindow.ProcessName,
            timeoutMs: 1000
        );
        var openedChildScreenshotPath = screenshotStore.SaveEvidencePng(
            run,
            "probe_notch_profile_window",
            capturer.CaptureWindowPngBytes(openedChildWindow.Hwnd)
        );
        var openedChildWindowBytes = capturer.CaptureWindowPngBytes(openedChildWindow.Hwnd);
        var childCaptureTargets = CaptureChildWindowTargets(run, screenshotStore, openedChildWindowBytes, childWindowProfile);
        var (closeMode, childWindowClosed) = CloseChildWindow(
            controller,
            childLocator,
            openedChildWindow,
            childTitleContains,
            defineWindow.ProcessName,
            childWindowProfile
        );
        string? defineWindowAfterCloseScreenshotPath = null;
        if (childWindowClosed)
        {
            var returnedWindow = new TestlabWindowLocator().Find();
            controller.Activate(returnedWindow.Hwnd);
            Thread.Sleep(120);
            defineWindowAfterCloseScreenshotPath = screenshotStore.SaveEvidencePng(
                run,
                "probe_define_notch_profiles_window_after_close",
                capturer.CaptureWindowPngBytes(returnedWindow.Hwnd)
            );
        }

        var result = new NotchProfilesEntryProbeResult(
            MainWindowTitle: mainWindow.Title,
            DefineNotchProfilesTitle: defineWindow.Title,
            TargetRowIndex: rowIndex,
            EntryClickPointsWindow: entryClickSequence,
            Opened: opened,
            ProfilePath: profileStore.ProfilePath,
            DefineNotchProfilesScreenshotPath: defineWindowScreenshotPath,
            OpenedChildScreenshotPath: openedChildScreenshotPath,
            ChildCaptureTargets: childCaptureTargets,
            CloseMode: closeMode,
            ChildWindowClosed: childWindowClosed,
            DefineNotchProfilesAfterCloseScreenshotPath: defineWindowAfterCloseScreenshotPath
        );

        var resultPath = Path.Combine(run.RunDirectory, "notch_profile_entry_probe.json");
        File.WriteAllText(resultPath, result.ToJson(), new UTF8Encoding(false));
        TestlabDebugMarkers.WritePhase("probe.notch_profile_entry.completed", run.RunDirectory, opened.ToDebugText());
        return result;
    }

    private static NotchProfileCaptureTargetResult[] CaptureChildWindowTargets(
        RunContext run,
        ScreenshotStore screenshotStore,
        byte[] childWindowBytes,
        WorkstationChildWindowProfile? childWindowProfile
    )
    {
        var targets = childWindowProfile?.CaptureTargets ?? [];
        if (targets.Length == 0)
        {
            return [];
        }

        var results = new List<NotchProfileCaptureTargetResult>();
        foreach (var target in targets)
        {
            if (target.RoiWindow is not BBox roiWindow)
            {
                continue;
            }

            var bytes = ImageCropper.TryCropToPngBytes(childWindowBytes, roiWindow) ?? childWindowBytes;
            var path = screenshotStore.SaveEvidencePng(
                run,
                $"probe_notch_profile_{WorkstationProfileKeys.Normalize(target.Key)}",
                bytes
            );
            results.Add(new NotchProfileCaptureTargetResult(target.Key, roiWindow, path));
        }

        return results.ToArray();
    }

    private static (string CloseMode, bool Closed) CloseChildWindow(
        WindowController controller,
        TestlabChildWindowLocator childLocator,
        TestlabWindowInfo openedChildWindow,
        string childTitleContains,
        string? processName,
        WorkstationChildWindowProfile? childWindowProfile
    )
    {
        controller.Activate(openedChildWindow.Hwnd);
        Thread.Sleep(120);

        var closeMode = "alt_f4";
        if (childWindowProfile?.CloseClickPoint is WindowPoint closeClickPoint)
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
}

public sealed record NotchProfilesEntryProbeResult(
    string MainWindowTitle,
    string DefineNotchProfilesTitle,
    int TargetRowIndex,
    WindowPoint[] EntryClickPointsWindow,
    TestlabChildWindowOpenResult Opened,
    string ProfilePath,
    string DefineNotchProfilesScreenshotPath,
    string OpenedChildScreenshotPath,
    IReadOnlyList<NotchProfileCaptureTargetResult> ChildCaptureTargets,
    string CloseMode,
    bool ChildWindowClosed,
    string? DefineNotchProfilesAfterCloseScreenshotPath
)
{
    public string ToJson()
        => System.Text.Json.JsonSerializer.Serialize(this, JsonOptions.Default);
}

public sealed record NotchProfileCaptureTargetResult(
    string Key,
    BBox RoiWindow,
    string ScreenshotPath
);
