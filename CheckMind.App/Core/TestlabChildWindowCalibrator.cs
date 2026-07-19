using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CheckMind.App.Core;

public sealed class TestlabChildWindowCalibrator
{
    private const string NotchProfilePagingPreparationMode = "activation_foreground";
    private const string SineSetupChannelSafetyParametersActionKey = "sine_setup_channel_safety_parameters";

    public async Task CalibrateSineSetupChannelSafetyParametersAsync(RunContext run)
    {
        var locator = new TestlabWindowLocator();
        var controller = new WindowController();
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"Profile not found: {store.ProfilePath}");
        }

        var profile = store.Load();
        using var hotkey = new HotkeyListener(Win32Native.VK_F8);

        System.Windows.MessageBox.Show(
            "程序将记录 Sine Setup 页面中 [Channel safety parameters] 的恢复点击点，供正式链路在进入 Notch Profile 前后显式切回该基面。",
            "CheckMind - 标定 Channel safety parameters",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        var mainWindow = locator.Find();
        controller.Maximize(mainWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(100);

        var openMenuClick = await CapturePointAsync(
            locator,
            controller,
            hotkey,
            "请将鼠标移动到“切回 Channel safety parameters”所需的第 1 个点击位置，然后按 F8。"
        );
        controller.ClickWindowPoint(mainWindow.Hwnd, openMenuClick);
        await Task.Delay(180);

        var targetClick = await CapturePointAsync(
            locator,
            controller,
            hotkey,
            "请将鼠标移动到“切回 Channel safety parameters”所需的第 2 个点击位置，然后按 F8。"
        );

        profile = SetSineSetupChannelSafetyParameters(profile, openMenuClick, targetClick);
        store.Save(profile);

        TestlabDebugMarkers.WritePhase(
            "calib.sine_setup_channel_safety_parameters_written",
            run.RunDirectory,
            $"profile={store.ProfilePath};sequence=({openMenuClick.X},{openMenuClick.Y})->({targetClick.X},{targetClick.Y})"
        );

        System.Windows.MessageBox.Show(
            $"已完成 Channel safety parameters 标定。{Environment.NewLine}{store.ProfilePath}",
            "CheckMind - 标定完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    public async Task CalibrateDefineNotchProfilesAsync(RunContext run)
    {
        var locator = new TestlabWindowLocator();
        var controller = new WindowController();
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"Profile not found: {store.ProfilePath}");
        }

        var profile = store.Load();
        using var hotkey = new HotkeyListener(Win32Native.VK_F8);

        System.Windows.MessageBox.Show(
            "\u5c06\u4f9d\u6b21\u6807\u5b9a Define notch profiles \u7684\u4e24\u6b65\u5165\u53e3\u70b9\u51fb\u3001\u5217\u8868 ROI\u3001\u7b2c 1 \u884c\u951a\u70b9\u3001\u7b2c 2 \u884c\u951a\u70b9\u3001\u4ee5\u53ca Edit \u6309\u94ae\u70b9\u4f4d\u3002",
            "CheckMind - \u6807\u5b9a Define notch profiles",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        var mainWindow = locator.Find();
        controller.Maximize(mainWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(100);

        var menuClick = await CapturePointAsync(
            locator,
            controller,
            hotkey,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230 Sine Setup \u9875\u9762\u7684 [Channel safety parameters]\uff0c\u7136\u540e\u6309 F8\u3002"
        );
        controller.ClickWindowPoint(mainWindow.Hwnd, menuClick);
        await Task.Delay(180);

        var dropdownClick = await CapturePointAsync(
            locator,
            controller,
            hotkey,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230\u4e0b\u62c9\u5217\u8868\u4e2d\u7684 [Define notch profiles]\uff0c\u7136\u540e\u6309 F8\u3002"
        );
        controller.ClickWindowPoint(mainWindow.Hwnd, dropdownClick);
        await Task.Delay(600);

        var defineWindow = locator.Find();
        controller.Maximize(defineWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(defineWindow.Hwnd);
        await Task.Delay(80);

        var listTopLeft = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            defineWindow,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230\u5217\u8868 ROI \u7684\u5de6\u4e0a\u89d2\uff0c\u7136\u540e\u6309 F8\u3002"
        );
        var listBottomRight = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            defineWindow,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230\u5217\u8868 ROI \u7684\u53f3\u4e0b\u89d2\uff0c\u7136\u540e\u6309 F8\u3002"
        );
        var firstRowAnchor = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            defineWindow,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230\u7b2c 1 \u884c\u53ef\u70b9\u51fb\u4e2d\u5fc3\uff0c\u7136\u540e\u6309 F8\u3002"
        );
        var secondRowAnchor = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            defineWindow,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230\u7b2c 2 \u884c\u53ef\u70b9\u51fb\u4e2d\u5fc3\uff0c\u7136\u540e\u6309 F8\u3002"
        );
        var editClick = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            defineWindow,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230 Edit \u6309\u94ae\u4e2d\u5fc3\uff0c\u7136\u540e\u6309 F8\u3002"
        );

        var listRoi = CreateRoi(listTopLeft, listBottomRight);
        var rowHeight = secondRowAnchor.Y - firstRowAnchor.Y;
        if (rowHeight <= 0)
        {
            throw new InvalidOperationException(
                $"\u7b2c 2 \u884c\u951a\u70b9\u5fc5\u987b\u4f4d\u4e8e\u7b2c 1 \u884c\u951a\u70b9\u4e4b\u4e0b\u3002first=({firstRowAnchor.X},{firstRowAnchor.Y}) second=({secondRowAnchor.X},{secondRowAnchor.Y})"
            );
        }

        profile = SetDefineNotchProfiles(profile, menuClick, dropdownClick, listRoi, firstRowAnchor, rowHeight, editClick);
        profile = SetSineSetupChannelSafetyParameters(profile, menuClick);
        store.Save(profile);

        TestlabDebugMarkers.WritePhase(
            "calib.child_window.define_notch_profiles_written",
            run.RunDirectory,
            $"profile={store.ProfilePath};listRoi=({listRoi.X},{listRoi.Y},{listRoi.Width},{listRoi.Height});rowHeight={rowHeight};firstRow=({firstRowAnchor.X},{firstRowAnchor.Y});edit=({editClick.X},{editClick.Y})"
        );

        System.Windows.MessageBox.Show(
            $"\u5df2\u5b8c\u6210 Define notch profiles \u6807\u5b9a\u3002{Environment.NewLine}{store.ProfilePath}",
            "CheckMind - \u6807\u5b9a\u5b8c\u6210",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    public async Task CalibrateDefineNotchProfilesLayoutSignatureAsync(RunContext run)
    {
        var locator = new TestlabWindowLocator();
        var childLocator = new TestlabChildWindowLocator();
        var controller = new WindowController();
        var capturer = new ScreenCapture();
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"Profile not found: {store.ProfilePath}");
        }

        var profile = store.Load();
        var defineWindowProfile = profile.FindChildWindowProfile("define_notch_profiles")
            ?? throw new InvalidOperationException("Profile missing ChildWindows.define_notch_profiles.");
        var listTarget = defineWindowProfile.FindListTarget("notch_profiles_list")
            ?? throw new InvalidOperationException("Profile missing define_notch_profiles.listTargets.notch_profiles_list.");
        if (listTarget.RoiWindow is not BBox listRoi)
        {
            throw new InvalidOperationException("Profile missing notch_profiles_list.RoiWindow.");
        }

        if (listTarget.ActionClickPoint is not WindowPoint actionClickPoint)
        {
            throw new InvalidOperationException("Profile missing notch_profiles_list.ActionClickPoint.");
        }

        if (listTarget.FirstRowAnchor is not WindowPoint firstRowAnchor)
        {
            throw new InvalidOperationException("Profile missing notch_profiles_list.FirstRowAnchor.");
        }

        if (listTarget.RowHeight is not int rowHeight || rowHeight <= 0)
        {
            throw new InvalidOperationException("Profile missing notch_profiles_list.RowHeight.");
        }

        var entryClickSequence = defineWindowProfile.GetOpenClickSequence();
        if (entryClickSequence.Length == 0)
        {
            throw new InvalidOperationException("Profile missing define_notch_profiles.openClickPoint / openClickSequence.");
        }

        System.Windows.MessageBox.Show(
            "程序将自动打开并最大化 Define notch profiles。请确认列表处于正式运行的标准布局，特别是列表首屏、行高、首行位置与 Edit 按钮位置均已稳定，然后点击确定采集布局签名。",
            "CheckMind - 标定 Notch Profiles 布局签名",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        var mainWindow = locator.Find();
        controller.Maximize(mainWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(100);

        var (restoreSequence, restoreSource) = ResolveSineSetupChannelSafetyRestoreSequence(profile);
        foreach (var restorePoint in restoreSequence)
        {
            controller.ClickWindowPoint(mainWindow.Hwnd, restorePoint);
            await Task.Delay(180);
        }
        await Task.Delay(360);
        TestlabDebugMarkers.WritePhase(
            "calib.child_window.define_notch_profiles_layout_signature_sine_setup_restored",
            run.RunDirectory,
            $"source={restoreSource};sequence={FormatWindowPointSequence(restoreSequence)}"
        );

        TestlabDebugMarkers.WritePhase(
            "calib.child_window.define_notch_profiles_layout_signature_open_begin",
            run.RunDirectory
        );

        static bool LooksLikeOpenedDefineWindow(TestlabWindowInfo candidate, TestlabWindowInfo mainWindow)
            => candidate.Hwnd != mainWindow.Hwnd ||
               candidate.Title.Contains("Define notch profiles", StringComparison.OrdinalIgnoreCase);

        TestlabWindowInfo? defineWindowMaybe = null;
        const int openAttemptCount = 2;
        const int openPollTimeoutMs = 4000;
        for (var openAttempt = 0; openAttempt < openAttemptCount && defineWindowMaybe is null; openAttempt++)
        {
            controller.Activate(mainWindow.Hwnd);
            await Task.Delay(100);
            foreach (var point in entryClickSequence)
            {
                controller.ClickWindowPoint(mainWindow.Hwnd, point);
                await Task.Delay(180);
            }

            TestlabDebugMarkers.WritePhase(
                "calib.child_window.define_notch_profiles_layout_signature_open_click_sequence_done",
                run.RunDirectory,
                $"attempt={openAttempt}"
            );

            var waitStart = DateTime.UtcNow;
            while ((DateTime.UtcNow - waitStart).TotalMilliseconds < openPollTimeoutMs)
            {
                try
                {
                    var currentWindow = locator.Find();
                    if (LooksLikeOpenedDefineWindow(currentWindow, mainWindow))
                    {
                        defineWindowMaybe = currentWindow;
                        break;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or TestlabWindowStateException)
                {
                }
                await Task.Delay(150);
            }

            if (defineWindowMaybe is null)
            {
                TestlabDebugMarkers.WritePhase(
                    "calib.child_window.define_notch_profiles_layout_signature_open_wait_retry",
                    run.RunDirectory,
                    $"attempt={openAttempt};timeoutMs={openPollTimeoutMs}"
                );
            }
        }

        TestlabWindowInfo defineWindow;
        if (defineWindowMaybe is not null)
        {
            defineWindow = defineWindowMaybe.Value;
            TestlabDebugMarkers.WritePhase(
                "calib.child_window.define_notch_profiles_layout_signature_open_found",
                run.RunDirectory,
                $"hwnd=0x{defineWindow.Hwnd.ToInt64():X};title={defineWindow.Title}"
            );
        }
        else
        {
            defineWindow = locator.Find();
            TestlabDebugMarkers.WritePhase(
                "calib.child_window.define_notch_profiles_layout_signature_open_current_window_fallback",
                run.RunDirectory,
                $"hwnd=0x{defineWindow.Hwnd.ToInt64():X};title={defineWindow.Title}"
            );
        }
        controller.Activate(defineWindow.Hwnd);
        await Task.Delay(80);
        controller.Maximize(defineWindow.Hwnd);
        await Task.Delay(120);
        controller.Activate(defineWindow.Hwnd);
        await Task.Delay(250);
        try
        {
            var defineWindowAfterMax = locator.Find();
            if (LooksLikeOpenedDefineWindow(defineWindowAfterMax, mainWindow))
            {
                defineWindow = defineWindowAfterMax;
                TestlabDebugMarkers.WritePhase(
                    "calib.child_window.define_notch_profiles_layout_signature_open_refound",
                    run.RunDirectory,
                    $"hwnd=0x{defineWindow.Hwnd.ToInt64():X};title={defineWindow.Title}"
                );
            }
            else
            {
                TestlabDebugMarkers.WritePhase(
                    "calib.child_window.define_notch_profiles_layout_signature_open_refind_fallback",
                    run.RunDirectory,
                    $"hwnd=0x{defineWindow.Hwnd.ToInt64():X};title={defineWindow.Title}"
                );
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TestlabWindowStateException)
        {
            TestlabDebugMarkers.WritePhase(
                "calib.child_window.define_notch_profiles_layout_signature_open_refind_failed",
                run.RunDirectory,
                $"error={ex.Message};hwnd=0x{defineWindow.Hwnd.ToInt64():X};title={defineWindow.Title}"
            );
        }

        var confirmResult = System.Windows.MessageBox.Show(
            "请确认当前已处于 Define notch profiles 页面，且列表已恢复到正式运行时的标准布局。若你本来就已经在该页面，可直接核对后点击“确定”；若列表首屏、行高或 Edit 区域仍在变化，请先手工恢复后再点击“确定”；若未准备好，请点“取消”。",
            "CheckMind - 手工确认列表布局",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information
        );
        if (confirmResult != MessageBoxResult.OK)
        {
            throw new OperationCanceledException("Notch Profiles 布局签名标定已取消。");
        }
        TestlabDebugMarkers.WritePhase(
            "calib.child_window.define_notch_profiles_layout_signature_confirmed",
            run.RunDirectory
        );

        var verifyRoi = GetNotchProfilesLayoutVerifyRoiWindow(listRoi, firstRowAnchor, rowHeight, actionClickPoint);
        var evidenceDir = Path.Combine(run.RunDirectory, "screenshots", "evidence");
        Directory.CreateDirectory(evidenceDir);

        byte[]? windowBytes = null;
        string? rawWindowPath = null;
        string? captureError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var currentWindow = locator.Find();
                if (LooksLikeOpenedDefineWindow(currentWindow, mainWindow))
                {
                    defineWindow = currentWindow;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or TestlabWindowStateException)
            {
                captureError = ex.Message;
            }
            controller.Activate(defineWindow.Hwnd);
            await Task.Delay(attempt == 0 ? 120 : 220);

            TestlabDebugMarkers.WritePhase(
                "calib.child_window.define_notch_profiles_layout_signature_capture_begin",
                run.RunDirectory,
                $"attempt={attempt};hwnd=0x{defineWindow.Hwnd.ToInt64():X};verifyRoi=({verifyRoi.X},{verifyRoi.Y},{verifyRoi.Width},{verifyRoi.Height})"
            );

            try
            {
                windowBytes = capturer.CaptureWindowPngBytes(defineWindow.Hwnd);
                rawWindowPath = Path.Combine(evidenceDir, $"define_notch_profiles_layout_signature_window_attempt_{attempt}.png");
                File.WriteAllBytes(rawWindowPath, windowBytes);

                TestlabDebugMarkers.WritePhase(
                    "calib.child_window.define_notch_profiles_layout_signature_capture_ok",
                    run.RunDirectory,
                    $"attempt={attempt};bytes={windowBytes.Length};evidence={rawWindowPath}"
                );
                break;
            }
            catch (Exception ex) when (ex is InvalidOperationException)
            {
                captureError = ex.Message;
                TestlabDebugMarkers.WritePhase(
                    "calib.child_window.define_notch_profiles_layout_signature_capture_failed",
                    run.RunDirectory,
                    $"attempt={attempt};error={ex.Message}"
                );
                await Task.Delay(220);
            }
        }

        if (windowBytes is null || windowBytes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Unable to capture Define notch profiles layout signature window bytes. lastError={captureError ?? "<none>"}"
            );
        }

        var verifyBytes = ImageCropper.TryCropToPngBytes(windowBytes, verifyRoi);
        if ((verifyBytes is null || verifyBytes.Length == 0) &&
            Win32Native.GetWindowRect(defineWindow.Hwnd, out var rect))
        {
            try
            {
                verifyBytes = capturer.CaptureRegionPngBytes(
                    rect.Left + verifyRoi.X,
                    rect.Top + verifyRoi.Y,
                    verifyRoi.Width,
                    verifyRoi.Height
                );
                TestlabDebugMarkers.WritePhase(
                    "calib.child_window.define_notch_profiles_layout_signature_region_fallback",
                    run.RunDirectory,
                    $"screen=({rect.Left + verifyRoi.X},{rect.Top + verifyRoi.Y},{verifyRoi.Width},{verifyRoi.Height})"
                );
            }
            catch (Exception ex) when (ex is InvalidOperationException)
            {
                captureError = ex.Message;
                TestlabDebugMarkers.WritePhase(
                    "calib.child_window.define_notch_profiles_layout_signature_region_fallback_failed",
                    run.RunDirectory,
                    $"error={ex.Message}"
                );
            }
        }

        if (verifyBytes is null || verifyBytes.Length == 0)
        {
            var reportPath = Path.Combine(run.RunDirectory, "define_notch_profiles_layout_signature_capture_report.json");
            var reportJson = JsonSerializer.Serialize(new
            {
                profilePath = store.ProfilePath,
                verifyRoi,
                rawWindowPath,
                lastError = captureError,
                hwnd = $"0x{defineWindow.Hwnd.ToInt64():X}"
            });
            File.WriteAllText(reportPath, reportJson, new UTF8Encoding(false));

            TestlabDebugMarkers.WritePhase(
                "calib.child_window.define_notch_profiles_layout_signature_capture_report_written",
                run.RunDirectory,
                $"report={reportPath}"
            );
            throw new InvalidOperationException("Unable to capture Define notch profiles layout signature bytes.");
        }

        var verifySha256 = ComputeImageContentSha256Hex(verifyBytes);
        profile = SetDefineNotchProfilesLayoutSignature(profile, verifyRoi, verifySha256);
        store.Save(profile);

        var evidencePath = Path.Combine(evidenceDir, "define_notch_profiles_layout_signature_verify.png");
        File.WriteAllBytes(evidencePath, verifyBytes);

        TestlabDebugMarkers.WritePhase(
            "calib.child_window.define_notch_profiles_layout_signature_written",
            run.RunDirectory,
            $"profile={store.ProfilePath};verifyRoi=({verifyRoi.X},{verifyRoi.Y},{verifyRoi.Width},{verifyRoi.Height});sha={verifySha256};evidence={evidencePath}"
        );
    }

    public async Task CalibrateNotchProfileAsync(RunContext run)
    {
        var locator = new TestlabWindowLocator();
        var childLocator = new TestlabChildWindowLocator();
        var controller = new WindowController();
        var automation = new TestlabChildWindowAutomation(childLocator, controller);
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"Profile not found: {store.ProfilePath}");
        }

        var profile = store.Load();
        var defineNotchWindow = profile.FindChildWindowProfile("define_notch_profiles")
            ?? throw new InvalidOperationException("Profile missing ChildWindows.define_notch_profiles.");
        var listTarget = defineNotchWindow.FindListTarget("notch_profiles_list")
            ?? throw new InvalidOperationException("Profile missing define_notch_profiles.listTargets.notch_profiles_list.");
        var entryClickSequence = defineNotchWindow.GetOpenClickSequence();
        if (entryClickSequence.Length == 0)
        {
            throw new InvalidOperationException("Profile missing define_notch_profiles.openClickSequence / openClickPoint.");
        }

        var rowIndexRaw = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_PROFILE_INDEX") ?? "1").Trim();
        if (!int.TryParse(rowIndexRaw, out var rowIndex) || rowIndex <= 0)
        {
            throw new InvalidOperationException($"Invalid CHECKMIND_NOTCH_PROFILE_INDEX: {rowIndexRaw}");
        }

        var childTitleContains = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_PROFILE_TITLE_CONTAINS") ?? "Notch Profile").Trim();
        if (string.IsNullOrWhiteSpace(childTitleContains))
        {
            childTitleContains = "Notch Profile";
        }

        using var hotkey = new HotkeyListener(Win32Native.VK_F8);

        System.Windows.MessageBox.Show(
            "\u7a0b\u5e8f\u5c06\u81ea\u52a8\u6253\u5f00\u6307\u5b9a\u5e8f\u53f7\u7684 Notch Profile\u3002\u8bf7\u518d\u4f9d\u6b21\u6807\u5b9a\u957f\u8868 ROI \u548c\u5173\u95ed\u6309\u94ae\u70b9\u4f4d\u3002",
            "CheckMind - \u6807\u5b9a Notch Profile",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        var mainWindow = locator.Find();
        controller.Maximize(mainWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(100);

        foreach (var point in entryClickSequence)
        {
            controller.ClickWindowPoint(mainWindow.Hwnd, point);
            await Task.Delay(180);
        }
        await Task.Delay(600);

        var defineWindow = locator.Find();
        controller.Maximize(defineWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(defineWindow.Hwnd);
        await Task.Delay(80);

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
        await Task.Delay(80);

        var tableTopLeft = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            notchWindow,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230 Notch Profile \u957f\u8868 ROI \u7684\u5de6\u4e0a\u89d2\uff0c\u7136\u540e\u6309 F8\u3002"
        );
        var tableBottomRight = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            notchWindow,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230 Notch Profile \u957f\u8868 ROI \u7684\u53f3\u4e0b\u89d2\uff0c\u7136\u540e\u6309 F8\u3002"
        );
        var closeClick = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            notchWindow,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230 Notch Profile \u5173\u95ed\u6309\u94ae\u4e2d\u5fc3\uff0c\u7136\u540e\u6309 F8\u3002"
        );

        var tableRoi = CreateRoi(tableTopLeft, tableBottomRight);
        profile = SetNotchProfile(profile, tableRoi, closeClick);
        store.Save(profile);

        TestlabDebugMarkers.WritePhase(
            "calib.child_window.notch_profile_written",
            run.RunDirectory,
            $"profile={store.ProfilePath};row={rowIndex};tableRoi=({tableRoi.X},{tableRoi.Y},{tableRoi.Width},{tableRoi.Height});close=({closeClick.X},{closeClick.Y});opened={opened.ChildWindowTitle}"
        );

        controller.ClickWindowPoint(notchWindow.Hwnd, closeClick);
        await Task.Delay(250);

        System.Windows.MessageBox.Show(
            $"\u5df2\u5b8c\u6210 Notch Profile \u6807\u5b9a\u3002{Environment.NewLine}{store.ProfilePath}",
            "CheckMind - \u6807\u5b9a\u5b8c\u6210",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    public async Task CalibrateNotchProfilePagingFocusAsync(RunContext run)
    {
        var locator = new TestlabWindowLocator();
        var childLocator = new TestlabChildWindowLocator();
        var controller = new WindowController();
        var automation = new TestlabChildWindowAutomation(childLocator, controller);
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"Profile not found: {store.ProfilePath}");
        }

        var profile = store.Load();
        var defineNotchWindow = profile.FindChildWindowProfile("define_notch_profiles")
            ?? throw new InvalidOperationException("Profile missing ChildWindows.define_notch_profiles.");
        var listTarget = defineNotchWindow.FindListTarget("notch_profiles_list")
            ?? throw new InvalidOperationException("Profile missing define_notch_profiles.listTargets.notch_profiles_list.");
        var notchProfileWindow = profile.FindChildWindowProfile("notch_profile")
            ?? throw new InvalidOperationException("Profile missing ChildWindows.notch_profile.");
        var tableScanTarget = notchProfileWindow.FindCaptureTarget("table_scan")
            ?? throw new InvalidOperationException("Profile missing notch_profile.captureTargets.table_scan.");
        if (tableScanTarget.RoiWindow is not BBox tableRoi)
        {
            throw new InvalidOperationException("Profile missing notch_profile.captureTargets.table_scan.RoiWindow.");
        }

        var entryClickSequence = defineNotchWindow.GetOpenClickSequence();
        if (entryClickSequence.Length == 0)
        {
            throw new InvalidOperationException("Profile missing define_notch_profiles.openClickSequence / openClickPoint.");
        }

        var rowIndexRaw = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_PROFILE_INDEX") ?? "1").Trim();
        if (!int.TryParse(rowIndexRaw, out var rowIndex) || rowIndex <= 0)
        {
            throw new InvalidOperationException($"Invalid CHECKMIND_NOTCH_PROFILE_INDEX: {rowIndexRaw}");
        }

        var childTitleContains = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_PROFILE_TITLE_CONTAINS") ?? "Notch Profile").Trim();
        if (string.IsNullOrWhiteSpace(childTitleContains))
        {
            childTitleContains = "Notch Profile";
        }

        using var hotkey = new HotkeyListener(Win32Native.VK_F8);

        System.Windows.MessageBox.Show(
            "\u7a0b\u5e8f\u5c06\u81ea\u52a8\u6253\u5f00\u6307\u5b9a\u5e8f\u53f7\u7684 Notch Profile\u3002\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230\u201c\u771f\u6b63\u80fd\u54cd\u5e94 PgUp/PgDn \u7ffb\u9875\u201d\u7684\u7136\u540e\u6309 F8\u3002",
            "CheckMind - \u6807\u5b9a Notch Profile \u7ffb\u9875\u7126\u70b9",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        var mainWindow = locator.Find();
        controller.Maximize(mainWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(100);

        foreach (var point in entryClickSequence)
        {
            controller.ClickWindowPoint(mainWindow.Hwnd, point);
            await Task.Delay(180);
        }
        await Task.Delay(600);

        var defineWindow = locator.Find();
        controller.Maximize(defineWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(defineWindow.Hwnd);
        await Task.Delay(80);

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
        await Task.Delay(80);

        var pagingFocus = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            notchWindow,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230 Notch Profile \u91cc\u771f\u6b63\u4f1a\u54cd\u5e94 PgUp/PgDn \u7684\u70b9\uff0c\u7136\u540e\u6309 F8\u3002"
        );

        profile = SetNotchProfilePagingFocus(profile, tableRoi, pagingFocus);
        store.Save(profile);

        TestlabDebugMarkers.WritePhase(
            "calib.child_window.notch_profile_paging_focus_written",
            run.RunDirectory,
            $"profile={store.ProfilePath};row={rowIndex};tableRoi=({tableRoi.X},{tableRoi.Y},{tableRoi.Width},{tableRoi.Height});focus=({pagingFocus.X},{pagingFocus.Y});opened={opened.ChildWindowTitle}"
        );

        if (notchProfileWindow.CloseClickPoint is WindowPoint closeClick)
        {
            controller.ClickWindowPoint(notchWindow.Hwnd, closeClick);
            await Task.Delay(250);
        }

        System.Windows.MessageBox.Show(
            $"\u5df2\u5b8c\u6210 Notch Profile \u7ffb\u9875\u7126\u70b9\u6807\u5b9a\u3002{Environment.NewLine}{store.ProfilePath}",
            "CheckMind - \u6807\u5b9a\u5b8c\u6210",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    public async Task CalibrateNotchProfilePagingActivationAsync(RunContext run)
    {
        var locator = new TestlabWindowLocator();
        var childLocator = new TestlabChildWindowLocator();
        var controller = new WindowController();
        var automation = new TestlabChildWindowAutomation(childLocator, controller);
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"Profile not found: {store.ProfilePath}");
        }

        var profile = store.Load();
        var defineNotchWindow = profile.FindChildWindowProfile("define_notch_profiles")
            ?? throw new InvalidOperationException("Profile missing ChildWindows.define_notch_profiles.");
        var listTarget = defineNotchWindow.FindListTarget("notch_profiles_list")
            ?? throw new InvalidOperationException("Profile missing define_notch_profiles.listTargets.notch_profiles_list.");
        var notchProfileWindow = profile.FindChildWindowProfile("notch_profile")
            ?? throw new InvalidOperationException("Profile missing ChildWindows.notch_profile.");
        var tableScanTarget = notchProfileWindow.FindCaptureTarget("table_scan")
            ?? throw new InvalidOperationException("Profile missing notch_profile.captureTargets.table_scan.");
        if (tableScanTarget.RoiWindow is not BBox tableRoi)
        {
            throw new InvalidOperationException("Profile missing notch_profile.captureTargets.table_scan.RoiWindow.");
        }

        var entryClickSequence = defineNotchWindow.GetOpenClickSequence();
        if (entryClickSequence.Length == 0)
        {
            throw new InvalidOperationException("Profile missing define_notch_profiles.openClickSequence / openClickPoint.");
        }

        var rowIndexRaw = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_PROFILE_INDEX") ?? "1").Trim();
        if (!int.TryParse(rowIndexRaw, out var rowIndex) || rowIndex <= 0)
        {
            throw new InvalidOperationException($"Invalid CHECKMIND_NOTCH_PROFILE_INDEX: {rowIndexRaw}");
        }

        var childTitleContains = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_PROFILE_TITLE_CONTAINS") ?? "Notch Profile").Trim();
        if (string.IsNullOrWhiteSpace(childTitleContains))
        {
            childTitleContains = "Notch Profile";
        }

        using var hotkey = new HotkeyListener(Win32Native.VK_F8);

        System.Windows.MessageBox.Show(
            "\u7a0b\u5e8f\u5c06\u81ea\u52a8\u6253\u5f00\u6307\u5b9a\u5e8f\u53f7\u7684 Notch Profile\u3002\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230\u201c\u4eba\u5de5\u70b9\u51fb\u540e\u624d\u4f1a\u8fdb\u5165\u53ef\u7ffb\u9875\u72b6\u6001\u201d\u7684\u771f\u5b9e\u8868\u683c\u4f4d\u7f6e\uff0c\u7136\u540e\u6309 F8\u3002",
            "CheckMind - \u6807\u5b9a Notch Profile \u7ffb\u9875\u6fc0\u6d3b\u70b9",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        var mainWindow = locator.Find();
        controller.Maximize(mainWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(100);

        foreach (var point in entryClickSequence)
        {
            controller.ClickWindowPoint(mainWindow.Hwnd, point);
            await Task.Delay(180);
        }
        await Task.Delay(600);

        var defineWindow = locator.Find();
        controller.Maximize(defineWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(defineWindow.Hwnd);
        await Task.Delay(80);

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
        await Task.Delay(80);

        var pagingActivation = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            notchWindow,
            "\u8bf7\u5c06\u9f20\u6807\u79fb\u52a8\u5230 Notch Profile \u91cc\u4eba\u5de5\u70b9\u51fb\u540e\u624d\u4f1a\u8fdb\u5165\u53ef\u7ffb\u9875\u72b6\u6001\u7684\u70b9\uff0c\u7136\u540e\u6309 F8\u3002"
        );

        profile = SetNotchProfilePagingActivation(profile, tableRoi, pagingActivation);
        store.Save(profile);

        TestlabDebugMarkers.WritePhase(
            "calib.child_window.notch_profile_paging_activation_written",
            run.RunDirectory,
            $"profile={store.ProfilePath};row={rowIndex};tableRoi=({tableRoi.X},{tableRoi.Y},{tableRoi.Width},{tableRoi.Height});activation=({pagingActivation.X},{pagingActivation.Y});opened={opened.ChildWindowTitle}"
        );

        if (notchProfileWindow.CloseClickPoint is WindowPoint closeClick)
        {
            controller.ClickWindowPoint(notchWindow.Hwnd, closeClick);
            await Task.Delay(250);
        }

        System.Windows.MessageBox.Show(
            $"\u5df2\u5b8c\u6210 Notch Profile \u7ffb\u9875\u6fc0\u6d3b\u70b9\u6807\u5b9a\u3002{Environment.NewLine}{store.ProfilePath}",
            "CheckMind - \u6807\u5b9a\u5b8c\u6210",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    public async Task CalibrateProfileEditorAsync(RunContext run)
    {
        var locator = new TestlabWindowLocator();
        var childLocator = new TestlabChildWindowLocator();
        var controller = new WindowController();
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"Profile not found: {store.ProfilePath}");
        }

        var profile = store.Load();
        using var hotkey = new HotkeyListener(Win32Native.VK_F8);

        System.Windows.MessageBox.Show(
            "程序将记录 Profile Editor 的入口点击点、最大化后的滑窗截图 ROI，以及关闭按钮点位。",
            "CheckMind - 标定 Profile Editor",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        var mainWindow = locator.Find();
        controller.Maximize(mainWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(100);

        var openClick = await CapturePointAsync(
            locator,
            controller,
            hotkey,
            "请将鼠标移动到 Sine Setup 页面的 [Edit reference profile] 入口，然后按 F8。"
        );

        var childWindow = await OpenSingleChildWindowAsync(
            mainWindow,
            openClick,
            controller,
            childLocator,
            "CHECKMIND_CHILD_WINDOW_TITLE_CONTAINS",
            "Profile Editor"
        );

        var tableScanRoi = await CaptureRoiAsyncFromWindow(
            controller,
            hotkey,
            childWindow,
            "请依次标定 Profile Editor 最大化后的滑窗截图区域。"
        );
        var closeClick = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            childWindow,
            "请将鼠标移动到 Profile Editor 关闭按钮中心，然后按 F8。"
        );

        profile = SetChildWindowCalibration(
            profile,
            windowKey: "profile_editor",
            openClick: openClick,
            closeClick: closeClick,
            tabClickTargets: null,
            captureTargets:
            [
                new WorkstationCaptureTarget("table_scan", tableScanRoi)
            ]
        );
        store.Save(profile);

        TestlabDebugMarkers.WritePhase(
            "calib.child_window.profile_editor_written",
            run.RunDirectory,
            $"profile={store.ProfilePath};open=({openClick.X},{openClick.Y});tableScan=({tableScanRoi.X},{tableScanRoi.Y},{tableScanRoi.Width},{tableScanRoi.Height});close=({closeClick.X},{closeClick.Y})"
        );

        System.Windows.MessageBox.Show(
            $"已完成 Profile Editor 标定。{Environment.NewLine}{store.ProfilePath}",
            "CheckMind - 标定完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    public async Task CalibrateProfileEditorPagingFocusAsync(RunContext run)
    {
        var locator = new TestlabWindowLocator();
        var childLocator = new TestlabChildWindowLocator();
        var controller = new WindowController();
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"Profile not found: {store.ProfilePath}");
        }

        var profile = store.Load();
        var profileEditorWindow = profile.FindChildWindowProfile("profile_editor")
            ?? throw new InvalidOperationException("Profile missing ChildWindows.profile_editor.");
        var tableScanTarget = profileEditorWindow.FindCaptureTarget("table_scan")
            ?? throw new InvalidOperationException("Profile missing profile_editor.captureTargets.table_scan.");
        if (tableScanTarget.RoiWindow is not BBox tableRoi)
        {
            throw new InvalidOperationException("Profile missing profile_editor.captureTargets.table_scan.RoiWindow.");
        }

        var entryClickSequence = profileEditorWindow.GetOpenClickSequence();
        if (entryClickSequence.Length == 0)
        {
            throw new InvalidOperationException("Profile missing profile_editor.openClickPoint / openClickSequence.");
        }

        using var hotkey = new HotkeyListener(Win32Native.VK_F8);

        System.Windows.MessageBox.Show(
            "程序将自动打开 Profile Editor。请将鼠标移动到“单击后不会进入文本编辑态、但 PgUp/PgDn 能生效”的空白位置，然后按 F8。",
            "CheckMind - 标定 Profile Editor 翻页焦点",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        var mainWindow = locator.Find();
        controller.Maximize(mainWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(100);

        foreach (var point in entryClickSequence)
        {
            controller.ClickWindowPoint(mainWindow.Hwnd, point);
            await Task.Delay(180);
        }
        await Task.Delay(600);

        var childWindow = childLocator.FindByTitleContains(
            "Profile Editor",
            processName: mainWindow.ProcessName,
            timeoutMs: 1500
        );
        controller.Activate(childWindow.Hwnd);
        await Task.Delay(80);
        controller.Maximize(childWindow.Hwnd);
        await Task.Delay(120);
        controller.Activate(childWindow.Hwnd);
        await Task.Delay(250);
        childWindow = childLocator.FindByTitleContains(
            "Profile Editor",
            processName: mainWindow.ProcessName,
            timeoutMs: 1000
        );

        var pagingFocus = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            childWindow,
            "请将鼠标移动到 Profile Editor 里真正会响应 PgUp/PgDn、且不会进入文本编辑态的空白位置，然后按 F8。"
        );

        profile = SetProfileEditorPagingFocus(profile, tableRoi, pagingFocus);
        store.Save(profile);

        TestlabDebugMarkers.WritePhase(
            "calib.child_window.profile_editor_paging_focus_written",
            run.RunDirectory,
            $"profile={store.ProfilePath};tableRoi=({tableRoi.X},{tableRoi.Y},{tableRoi.Width},{tableRoi.Height});focus=({pagingFocus.X},{pagingFocus.Y});opened={childWindow.Title}"
        );

        if (profileEditorWindow.CloseClickPoint is WindowPoint closeClick)
        {
            controller.ClickWindowPoint(childWindow.Hwnd, closeClick);
            await Task.Delay(250);
        }

        System.Windows.MessageBox.Show(
            $"已完成 Profile Editor 翻页焦点标定。{Environment.NewLine}{store.ProfilePath}",
            "CheckMind - 标定完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    public async Task CalibrateProfileEditorPagingActivationAsync(RunContext run)
    {
        var locator = new TestlabWindowLocator();
        var childLocator = new TestlabChildWindowLocator();
        var controller = new WindowController();
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"Profile not found: {store.ProfilePath}");
        }

        var profile = store.Load();
        var profileEditorWindow = profile.FindChildWindowProfile("profile_editor")
            ?? throw new InvalidOperationException("Profile missing ChildWindows.profile_editor.");
        var tableScanTarget = profileEditorWindow.FindCaptureTarget("table_scan")
            ?? throw new InvalidOperationException("Profile missing profile_editor.captureTargets.table_scan.");
        if (tableScanTarget.RoiWindow is not BBox tableRoi)
        {
            throw new InvalidOperationException("Profile missing profile_editor.captureTargets.table_scan.RoiWindow.");
        }

        var entryClickSequence = profileEditorWindow.GetOpenClickSequence();
        if (entryClickSequence.Length == 0)
        {
            throw new InvalidOperationException("Profile missing profile_editor.openClickPoint / openClickSequence.");
        }

        using var hotkey = new HotkeyListener(Win32Native.VK_F8);

        System.Windows.MessageBox.Show(
            "程序将自动打开 Profile Editor。请将鼠标移动到“人工点击后会进入可翻页状态，但不会进入文本编辑态”的空白位置，然后按 F8。",
            "CheckMind - 标定 Profile Editor 翻页激活点",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        var mainWindow = locator.Find();
        controller.Maximize(mainWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(100);

        foreach (var point in entryClickSequence)
        {
            controller.ClickWindowPoint(mainWindow.Hwnd, point);
            await Task.Delay(180);
        }
        await Task.Delay(600);

        var childWindow = childLocator.FindByTitleContains(
            "Profile Editor",
            processName: mainWindow.ProcessName,
            timeoutMs: 1500
        );
        controller.Activate(childWindow.Hwnd);
        await Task.Delay(80);
        controller.Maximize(childWindow.Hwnd);
        await Task.Delay(120);
        controller.Activate(childWindow.Hwnd);
        await Task.Delay(250);
        childWindow = childLocator.FindByTitleContains(
            "Profile Editor",
            processName: mainWindow.ProcessName,
            timeoutMs: 1000
        );

        var pagingActivation = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            childWindow,
            "请将鼠标移动到 Profile Editor 里用于人工激活翻页状态、且不会进入文本编辑态的空白位置，然后按 F8。"
        );

        profile = SetProfileEditorPagingActivation(profile, tableRoi, pagingActivation);
        store.Save(profile);

        TestlabDebugMarkers.WritePhase(
            "calib.child_window.profile_editor_paging_activation_written",
            run.RunDirectory,
            $"profile={store.ProfilePath};tableRoi=({tableRoi.X},{tableRoi.Y},{tableRoi.Width},{tableRoi.Height});activation=({pagingActivation.X},{pagingActivation.Y});opened={childWindow.Title}"
        );

        if (profileEditorWindow.CloseClickPoint is WindowPoint closeClick)
        {
            controller.ClickWindowPoint(childWindow.Hwnd, closeClick);
            await Task.Delay(250);
        }

        System.Windows.MessageBox.Show(
            $"已完成 Profile Editor 翻页激活点标定。{Environment.NewLine}{store.ProfilePath}",
            "CheckMind - 标定完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    public async Task CalibrateProfileEditorTopSignatureAsync(RunContext run)
    {
        var locator = new TestlabWindowLocator();
        var childLocator = new TestlabChildWindowLocator();
        var controller = new WindowController();
        var capturer = new ScreenCapture();
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"Profile not found: {store.ProfilePath}");
        }

        var profile = store.Load();
        var profileEditorWindow = profile.FindChildWindowProfile("profile_editor")
            ?? throw new InvalidOperationException("Profile missing ChildWindows.profile_editor.");
        var tableScanTarget = profileEditorWindow.FindCaptureTarget("table_scan")
            ?? throw new InvalidOperationException("Profile missing profile_editor.captureTargets.table_scan.");
        if (tableScanTarget.RoiWindow is not BBox tableRoi)
        {
            throw new InvalidOperationException("Profile missing profile_editor.captureTargets.table_scan.RoiWindow.");
        }

        var pagingActivation = tableScanTarget.PagingActivationPointWindow
            ?? tableScanTarget.PagingFocusPointWindow
            ?? throw new InvalidOperationException("Profile missing profile_editor table_scan paging activation/focus point.");

        var entryClickSequence = profileEditorWindow.GetOpenClickSequence();
        if (entryClickSequence.Length == 0)
        {
            throw new InvalidOperationException("Profile missing profile_editor.openClickPoint / openClickSequence.");
        }

        System.Windows.MessageBox.Show(
            "程序将自动打开并最大化 Profile Editor。请你手工把列表回到真正顶部，并保持为正式运行时的标准布局；确认已到顶部后，再点击确定采集顶部签名。",
            "CheckMind - 标定 Profile Editor 顶部签名",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        var mainWindow = locator.Find();
        controller.Maximize(mainWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(100);

        foreach (var point in entryClickSequence)
        {
            controller.ClickWindowPoint(mainWindow.Hwnd, point);
            await Task.Delay(180);
        }
        await Task.Delay(600);

        var childWindow = childLocator.FindByTitleContains(
            "Profile Editor",
            processName: mainWindow.ProcessName,
            timeoutMs: 1500
        );
        controller.Activate(childWindow.Hwnd);
        await Task.Delay(80);
        controller.Maximize(childWindow.Hwnd);
        await Task.Delay(120);
        controller.Activate(childWindow.Hwnd);
        await Task.Delay(250);
        childWindow = childLocator.FindByTitleContains(
            "Profile Editor",
            processName: mainWindow.ProcessName,
            timeoutMs: 1000
        );

        controller.ClickWindowPoint(childWindow.Hwnd, pagingActivation);
        await Task.Delay(120);
        controller.Activate(childWindow.Hwnd);
        await Task.Delay(120);

        var confirmResult = System.Windows.MessageBox.Show(
            "请现在手工将 Profile Editor 列表回到真正顶部。确认顶部序号已就位、且当前状态就是后续正式运行的起始页后，点击“确定”开始采集顶部签名；若未准备好，请点“取消”。",
            "CheckMind - 手工确认顶部",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information
        );
        if (confirmResult != MessageBoxResult.OK)
        {
            throw new OperationCanceledException("Profile Editor 顶部签名标定已取消。");
        }

        controller.Activate(childWindow.Hwnd);
        await Task.Delay(120);
        controller.ClickWindowPoint(childWindow.Hwnd, pagingActivation);
        await Task.Delay(120);
        var topWindowBytes = capturer.CaptureWindowPngBytes(childWindow.Hwnd);
        var verifyRoi = GetTopSerialVerifyRoiWindow(tableRoi);
        var verifyBytes = ImageCropper.TryCropToPngBytes(topWindowBytes, verifyRoi);
        if (verifyBytes is null || verifyBytes.Length == 0)
        {
            throw new InvalidOperationException("Unable to capture Profile Editor top signature bytes.");
        }

        var verifySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(verifyBytes));
        profile = SetProfileEditorTopSignature(profile, verifyRoi, verifySha256);
        store.Save(profile);

        var evidenceDir = Path.Combine(run.RunDirectory, "screenshots", "evidence");
        Directory.CreateDirectory(evidenceDir);
        var evidencePath = Path.Combine(evidenceDir, "profile_editor_top_signature_verify.png");
        File.WriteAllBytes(evidencePath, verifyBytes);

        TestlabDebugMarkers.WritePhase(
            "calib.child_window.profile_editor_top_signature_written",
            run.RunDirectory,
            $"profile={store.ProfilePath};verifyRoi=({verifyRoi.X},{verifyRoi.Y},{verifyRoi.Width},{verifyRoi.Height});sha={verifySha256};topConfirmedByUser=1;evidence={evidencePath}"
        );

        if (profileEditorWindow.CloseClickPoint is WindowPoint closeClick)
        {
            controller.ClickWindowPoint(childWindow.Hwnd, closeClick);
            await Task.Delay(250);
        }

        System.Windows.MessageBox.Show(
            $"已完成 Profile Editor 顶部签名标定。{Environment.NewLine}{store.ProfilePath}",
            "CheckMind - 标定完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    public async Task CalibrateAdvancedControlSetupAsync(RunContext run)
    {
        var locator = new TestlabWindowLocator();
        var childLocator = new TestlabChildWindowLocator();
        var controller = new WindowController();
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"Profile not found: {store.ProfilePath}");
        }

        var profile = store.Load();
        using var hotkey = new HotkeyListener(Win32Native.VK_F8);

        System.Windows.MessageBox.Show(
            "程序将记录 Advanced Control Setup 的入口点击点、3 个页签点击点、3 个页签各自截图 ROI、关闭按钮点位，以及返回 Sine Setup 后右侧 Control 栏 ROI。",
            "CheckMind - 标定 Advanced Control Setup",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        var mainWindow = locator.Find();
        controller.Maximize(mainWindow.Hwnd);
        await Task.Delay(250);
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(100);

        var openClick = await CapturePointAsync(
            locator,
            controller,
            hotkey,
            "请将鼠标移动到 Sine Setup 页面上的 [Advanced Control Setup] 入口，然后按 F8。"
        );

        var childWindow = await OpenSingleChildWindowAsync(
            mainWindow,
            openClick,
            controller,
            childLocator,
            "CHECKMIND_CHILD_WINDOW_TITLE_CONTAINS",
            "Advanced Control Setup"
        );

        var safetyTab = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            childWindow,
            "请将鼠标移动到 [Safety] 页签，然后按 F8。"
        );
        controller.ClickWindowPoint(childWindow.Hwnd, safetyTab);
        await Task.Delay(220);
        var safetyRoi = await CaptureRoiAsyncFromWindow(
            controller,
            hotkey,
            childWindow,
            "请依次标定 [Safety] 子页签的截图区域。"
        );
        var measurementsTab = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            childWindow,
            "请将鼠标移动到 [Measurements] 页签，然后按 F8。"
        );
        controller.ClickWindowPoint(childWindow.Hwnd, measurementsTab);
        await Task.Delay(220);
        var measurementsRoi = await CaptureRoiAsyncFromWindow(
            controller,
            hotkey,
            childWindow,
            "请依次标定 [Measurements] 子页签的截图区域。"
        );
        var throughputRecordingTab = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            childWindow,
            "请将鼠标移动到 [Throughput Recording] 页签，然后按 F8。"
        );
        controller.ClickWindowPoint(childWindow.Hwnd, throughputRecordingTab);
        await Task.Delay(220);
        var throughputRecordingRoi = await CaptureRoiAsyncFromWindow(
            controller,
            hotkey,
            childWindow,
            "请依次标定 [Throughput Recording] 子页签的截图区域。"
        );
        var closeClick = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            childWindow,
            "请将鼠标移动到 Advanced Control Setup 关闭按钮中心，然后按 F8。"
        );

        profile = SetChildWindowCalibration(
            profile,
            windowKey: "advanced_control_setup",
            openClick: openClick,
            closeClick: closeClick,
            tabClickTargets:
            [
                new WorkstationTabClickTarget("Safety", safetyTab),
                new WorkstationTabClickTarget("Measurements", measurementsTab),
                new WorkstationTabClickTarget("Throughput Recording", throughputRecordingTab)
            ],
            captureTargets:
            [
                new WorkstationCaptureTarget("safety", safetyRoi),
                new WorkstationCaptureTarget("measurements", measurementsRoi),
                new WorkstationCaptureTarget("throughput_recording", throughputRecordingRoi)
            ]
        );

        controller.ClickWindowPoint(childWindow.Hwnd, closeClick);
        await Task.Delay(450);

        var returnedMainWindow = locator.Find();
        controller.Maximize(returnedMainWindow.Hwnd);
        await Task.Delay(200);
        controller.Activate(returnedMainWindow.Hwnd);
        await Task.Delay(120);

        var controlPanelRoi = await CaptureRoiAsync(
            locator,
            controller,
            hotkey,
            "请在回到 Sine Setup 后，依次标定右侧 [Control] 栏截图区域。"
        );

        profile = SetPageCaptureTarget(profile, "Sine Setup", "control_panel", controlPanelRoi);
        store.Save(profile);

        TestlabDebugMarkers.WritePhase(
            "calib.child_window.advanced_control_setup_written",
            run.RunDirectory,
            $"profile={store.ProfilePath};open=({openClick.X},{openClick.Y});close=({closeClick.X},{closeClick.Y});safety=({safetyRoi.X},{safetyRoi.Y},{safetyRoi.Width},{safetyRoi.Height});measurements=({measurementsRoi.X},{measurementsRoi.Y},{measurementsRoi.Width},{measurementsRoi.Height});throughput=({throughputRecordingRoi.X},{throughputRecordingRoi.Y},{throughputRecordingRoi.Width},{throughputRecordingRoi.Height});controlPanel=({controlPanelRoi.X},{controlPanelRoi.Y},{controlPanelRoi.Width},{controlPanelRoi.Height})"
        );

        System.Windows.MessageBox.Show(
            $"已完成 Advanced Control Setup 标定。{Environment.NewLine}{store.ProfilePath}",
            "CheckMind - 标定完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private static async Task<WindowPoint> CapturePointAsync(
        TestlabWindowLocator locator,
        WindowController controller,
        HotkeyListener hotkey,
        string prompt
    )
    {
        System.Windows.MessageBox.Show(prompt, "CheckMind - \u5b50\u7a97\u53e3\u6807\u5b9a", MessageBoxButton.OK, MessageBoxImage.Information);
        await hotkey.WaitAsync(CancellationToken.None);

        var cursor = controller.GetCursorScreenPoint() ?? throw new InvalidOperationException("\u8bfb\u53d6\u9f20\u6807\u4f4d\u7f6e\u5931\u8d25\u3002");
        var mainWindow = locator.Find();
        controller.Activate(mainWindow.Hwnd);
        await Task.Delay(50);

        var localX = cursor.X - mainWindow.Rect.Left;
        var localY = cursor.Y - mainWindow.Rect.Top;
        if (localX < 0 || localY < 0 || localX >= mainWindow.Rect.Width || localY >= mainWindow.Rect.Height)
        {
            throw new InvalidOperationException(
                $"\u6807\u5b9a\u70b9\u4e0d\u5728 Testlab \u4e3b\u7a97\u53e3\u5185\u3002screen=({cursor.X},{cursor.Y}) win=({mainWindow.Rect.Left},{mainWindow.Rect.Top},{mainWindow.Rect.Width},{mainWindow.Rect.Height})"
            );
        }

        return new WindowPoint(localX, localY);
    }

    private static async Task<BBox> CaptureRoiAsync(
        TestlabWindowLocator locator,
        WindowController controller,
        HotkeyListener hotkey,
        string prompt
    )
    {
        var topLeft = await CapturePointAsync(
            locator,
            controller,
            hotkey,
            $"{prompt}{Environment.NewLine}第 1 步：请将鼠标移动到左上角，然后按 F8。"
        );
        var bottomRight = await CapturePointAsync(
            locator,
            controller,
            hotkey,
            $"{prompt}{Environment.NewLine}第 2 步：请将鼠标移动到右下角，然后按 F8。"
        );
        return CreateRoi(topLeft, bottomRight);
    }

    private static async Task<WindowPoint> CapturePointAsyncFromWindow(
        WindowController controller,
        HotkeyListener hotkey,
        TestlabWindowInfo window,
        string prompt
    )
    {
        System.Windows.MessageBox.Show(prompt, "CheckMind - \u5b50\u7a97\u53e3\u6807\u5b9a", MessageBoxButton.OK, MessageBoxImage.Information);
        await hotkey.WaitAsync(CancellationToken.None);

        var cursor = controller.GetCursorScreenPoint() ?? throw new InvalidOperationException("\u8bfb\u53d6\u9f20\u6807\u4f4d\u7f6e\u5931\u8d25\u3002");
        controller.Activate(window.Hwnd);
        await Task.Delay(50);

        var localX = cursor.X - window.Rect.Left;
        var localY = cursor.Y - window.Rect.Top;
        if (localX < 0 || localY < 0 || localX >= window.Rect.Width || localY >= window.Rect.Height)
        {
            throw new InvalidOperationException(
                $"\u6807\u5b9a\u70b9\u4e0d\u5728\u76ee\u6807\u7a97\u53e3\u5185\u3002screen=({cursor.X},{cursor.Y}) win=({window.Rect.Left},{window.Rect.Top},{window.Rect.Width},{window.Rect.Height})"
            );
        }

        return new WindowPoint(localX, localY);
    }

    private static async Task<BBox> CaptureRoiAsyncFromWindow(
        WindowController controller,
        HotkeyListener hotkey,
        TestlabWindowInfo window,
        string prompt
    )
    {
        var topLeft = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            window,
            $"{prompt}{Environment.NewLine}第 1 步：请将鼠标移动到左上角，然后按 F8。"
        );
        var bottomRight = await CapturePointAsyncFromWindow(
            controller,
            hotkey,
            window,
            $"{prompt}{Environment.NewLine}第 2 步：请将鼠标移动到右下角，然后按 F8。"
        );
        return CreateRoi(topLeft, bottomRight);
    }

    private static BBox CreateRoi(WindowPoint a, WindowPoint b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X, b.X);
        var bottom = Math.Max(a.Y, b.Y);
        return new BBox(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static async Task<TestlabWindowInfo> OpenSingleChildWindowAsync(
        TestlabWindowInfo mainWindow,
        WindowPoint openClick,
        WindowController controller,
        TestlabChildWindowLocator childLocator,
        string titleContainsEnvKey,
        string defaultTitleContains
    )
    {
        controller.ClickWindowPoint(mainWindow.Hwnd, openClick);
        await Task.Delay(600);

        var titleContains = (Environment.GetEnvironmentVariable(titleContainsEnvKey) ?? defaultTitleContains).Trim();
        if (string.IsNullOrWhiteSpace(titleContains))
        {
            titleContains = defaultTitleContains;
        }

        var childWindow = childLocator.FindByTitleContains(
            titleContains,
            processName: mainWindow.ProcessName,
            timeoutMs: 1500
        );

        controller.Activate(childWindow.Hwnd);
        await Task.Delay(80);
        controller.Maximize(childWindow.Hwnd);
        await Task.Delay(120);
        controller.Activate(childWindow.Hwnd);
        await Task.Delay(250);

        return childLocator.FindByTitleContains(
            titleContains,
            processName: mainWindow.ProcessName,
            timeoutMs: 1000
        );
    }

    private static WorkstationProfile SetDefineNotchProfiles(
        WorkstationProfile profile,
        WindowPoint menuClick,
        WindowPoint dropdownClick,
        BBox listRoi,
        WindowPoint firstRowAnchor,
        int rowHeight,
        WindowPoint editClick
    )
    {
        var children = (profile.ChildWindows ?? []).ToList();
        var keyNorm = WorkstationProfileKeys.Normalize("define_notch_profiles");
        var updated = false;
        for (var i = 0; i < children.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(children[i].WindowKey) != keyNorm)
            {
                continue;
            }

            children[i] = children[i] with
            {
                OpenClickPoint = menuClick,
                OpenClickSequence = [menuClick, dropdownClick],
                MustMaximize = true,
                ListTargets =
                [
                    new WorkstationListNavigationTarget(
                        Key: "notch_profiles_list",
                        RoiWindow: listRoi,
                        FirstRowAnchor: firstRowAnchor,
                        RowHeight: rowHeight,
                        ActionClickPoint: editClick
                    )
                ]
            };
            updated = true;
            break;
        }

        if (!updated)
        {
            children.Add(new WorkstationChildWindowProfile(
                WindowKey: "define_notch_profiles",
                OpenClickPoint: menuClick,
                OpenClickSequence: [menuClick, dropdownClick],
                MustMaximize: true,
                ListTargets:
                [
                    new WorkstationListNavigationTarget(
                        Key: "notch_profiles_list",
                        RoiWindow: listRoi,
                        FirstRowAnchor: firstRowAnchor,
                        RowHeight: rowHeight,
                        ActionClickPoint: editClick
                    )
                ]
            ));
        }

        return profile with { ChildWindows = children.ToArray() };
    }

    private static WorkstationProfile SetSineSetupChannelSafetyParameters(
        WorkstationProfile profile,
        WindowPoint firstClick,
        WindowPoint? secondClick = null
    )
    {
        var actions = (profile.DialogActions ?? []).ToList();
        var keyNorm = WorkstationProfileKeys.Normalize(SineSetupChannelSafetyParametersActionKey);
        WindowPoint[] clickSequence = secondClick is WindowPoint second
            ? [firstClick, second]
            : [firstClick];
        var updated = false;
        for (var i = 0; i < actions.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(actions[i].DialogKey) != keyNorm)
            {
                continue;
            }

            actions[i] = actions[i] with
            {
                ClickPoint = firstClick,
                ClickSequence = clickSequence
            };
            updated = true;
            break;
        }

        if (!updated)
        {
            actions.Add(new WorkstationDialogActionProfile(SineSetupChannelSafetyParametersActionKey, firstClick, clickSequence));
        }

        return profile with { DialogActions = actions.ToArray() };
    }

    private static (WindowPoint[] Sequence, string Source) ResolveSineSetupChannelSafetyRestoreSequence(WorkstationProfile profile)
    {
        var directSequence = profile.FindDialogAction(SineSetupChannelSafetyParametersActionKey)?.GetClickSequence();
        if (directSequence is not null && directSequence.Length > 0)
        {
            return (directSequence, "dialog_action_sequence");
        }

        throw new InvalidOperationException("profile 缺少 `Channel safety parameters` 恢复点击点；请先执行相关标定。");
    }

    private static string FormatWindowPointSequence(IReadOnlyList<WindowPoint> sequence)
        => string.Join("->", sequence.Select(static p => $"({p.X},{p.Y})"));

    private static WorkstationProfile SetChildWindowCalibration(
        WorkstationProfile profile,
        string windowKey,
        WindowPoint openClick,
        WindowPoint closeClick,
        WorkstationTabClickTarget[]? tabClickTargets,
        WorkstationCaptureTarget[]? captureTargets
    )
    {
        var children = (profile.ChildWindows ?? []).ToList();
        var keyNorm = WorkstationProfileKeys.Normalize(windowKey);
        var updated = false;
        for (var i = 0; i < children.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(children[i].WindowKey) != keyNorm)
            {
                continue;
            }

            children[i] = children[i] with
            {
                OpenClickPoint = openClick,
                MustMaximize = true,
                CloseClickPoint = closeClick,
                CaptureTargets = captureTargets,
                TabClickPoints = tabClickTargets
            };
            updated = true;
            break;
        }

        if (!updated)
        {
            children.Add(new WorkstationChildWindowProfile(
                WindowKey: windowKey,
                OpenClickPoint: openClick,
                MustMaximize: true,
                CloseClickPoint: closeClick,
                TabClickPoints: tabClickTargets,
                CaptureTargets: captureTargets,
                VerifyTargets: null
            ));
        }

        return profile with { ChildWindows = children.ToArray() };
    }

    private static WorkstationProfile SetPageCaptureTarget(
        WorkstationProfile profile,
        string tabName,
        string targetKey,
        BBox roi
    )
    {
        var pages = (profile.Pages ?? []).ToList();
        var tabNorm = WorkstationProfileKeys.Normalize(tabName);
        var keyNorm = WorkstationProfileKeys.Normalize(targetKey);

        for (var i = 0; i < pages.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(pages[i].TabName) != tabNorm)
            {
                continue;
            }

            var targets = (pages[i].CaptureTargets ?? []).ToList();
            var updated = false;
            for (var j = 0; j < targets.Count; j++)
            {
                if (WorkstationProfileKeys.Normalize(targets[j].Key) != keyNorm)
                {
                    continue;
                }

                targets[j] = targets[j] with { RoiWindow = roi };
                updated = true;
                break;
            }

            if (!updated)
            {
                targets.Add(new WorkstationCaptureTarget(targetKey, roi));
            }

            pages[i] = pages[i] with { CaptureTargets = targets.ToArray() };
            return profile with { Pages = pages.ToArray() };
        }

        pages.Add(new WorkstationPageProfile(
            TabName: tabName,
            CaptureTargets: [new WorkstationCaptureTarget(targetKey, roi)]
        ));
        return profile with { Pages = pages.ToArray() };
    }

    private static WorkstationProfile SetNotchProfile(
        WorkstationProfile profile,
        BBox tableRoi,
        WindowPoint closeClick
    )
    {
        var children = (profile.ChildWindows ?? []).ToList();
        var keyNorm = WorkstationProfileKeys.Normalize("notch_profile");
        var updated = false;
        for (var i = 0; i < children.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(children[i].WindowKey) != keyNorm)
            {
                continue;
            }

            var existingTarget = children[i].FindCaptureTarget("table_scan");
            var pagingFocusPoint = existingTarget?.PagingFocusPointWindow ?? ComputeDefaultPagingFocusPoint(tableRoi);
            var pagingActivationPoint = existingTarget?.PagingActivationPointWindow;
            var pagingPreparationMode = existingTarget?.PagingPreparationMode ?? NotchProfilePagingPreparationMode;
            children[i] = children[i] with
            {
                MustMaximize = true,
                CloseClickPoint = closeClick,
                CaptureTargets =
                [
                    new WorkstationCaptureTarget("table_scan", tableRoi, pagingFocusPoint, pagingActivationPoint, pagingPreparationMode)
                ]
            };
            updated = true;
            break;
        }

        if (!updated)
        {
            children.Add(new WorkstationChildWindowProfile(
                WindowKey: "notch_profile",
                MustMaximize: true,
                CloseClickPoint: closeClick,
                CaptureTargets:
                [
                    new WorkstationCaptureTarget("table_scan", tableRoi, ComputeDefaultPagingFocusPoint(tableRoi), null, NotchProfilePagingPreparationMode)
                ]
            ));
        }

        return profile with { ChildWindows = children.ToArray() };
    }

    private static WorkstationProfile SetNotchProfilePagingFocus(
        WorkstationProfile profile,
        BBox tableRoi,
        WindowPoint pagingFocus
    )
    {
        var children = (profile.ChildWindows ?? []).ToList();
        var keyNorm = WorkstationProfileKeys.Normalize("notch_profile");
        for (var i = 0; i < children.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(children[i].WindowKey) != keyNorm)
            {
                continue;
            }

            var targets = (children[i].CaptureTargets ?? []).ToList();
            var updated = false;
            for (var j = 0; j < targets.Count; j++)
            {
                if (WorkstationProfileKeys.Normalize(targets[j].Key) != WorkstationProfileKeys.Normalize("table_scan"))
                {
                    continue;
                }

                targets[j] = targets[j] with
                {
                    RoiWindow = targets[j].RoiWindow ?? tableRoi,
                    PagingFocusPointWindow = pagingFocus,
                    PagingPreparationMode = targets[j].PagingPreparationMode ?? NotchProfilePagingPreparationMode
                };
                updated = true;
                break;
            }

            if (!updated)
            {
                targets.Add(new WorkstationCaptureTarget("table_scan", tableRoi, pagingFocus, null, NotchProfilePagingPreparationMode));
            }

            children[i] = children[i] with { CaptureTargets = targets.ToArray() };
            return profile with { ChildWindows = children.ToArray() };
        }

        children.Add(new WorkstationChildWindowProfile(
            WindowKey: "notch_profile",
            MustMaximize: true,
            CaptureTargets:
            [
                new WorkstationCaptureTarget("table_scan", tableRoi, pagingFocus, null, NotchProfilePagingPreparationMode)
            ]
        ));
        return profile with { ChildWindows = children.ToArray() };
    }

    private static WorkstationProfile SetNotchProfilePagingActivation(
        WorkstationProfile profile,
        BBox tableRoi,
        WindowPoint pagingActivation
    )
    {
        var children = (profile.ChildWindows ?? []).ToList();
        var keyNorm = WorkstationProfileKeys.Normalize("notch_profile");
        for (var i = 0; i < children.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(children[i].WindowKey) != keyNorm)
            {
                continue;
            }

            var targets = (children[i].CaptureTargets ?? []).ToList();
            var updated = false;
            for (var j = 0; j < targets.Count; j++)
            {
                if (WorkstationProfileKeys.Normalize(targets[j].Key) != WorkstationProfileKeys.Normalize("table_scan"))
                {
                    continue;
                }

                targets[j] = targets[j] with
                {
                    RoiWindow = targets[j].RoiWindow ?? tableRoi,
                    PagingActivationPointWindow = pagingActivation,
                    PagingPreparationMode = targets[j].PagingPreparationMode ?? NotchProfilePagingPreparationMode
                };
                updated = true;
                break;
            }

            if (!updated)
            {
                targets.Add(new WorkstationCaptureTarget("table_scan", tableRoi, ComputeDefaultPagingFocusPoint(tableRoi), pagingActivation, NotchProfilePagingPreparationMode));
            }

            children[i] = children[i] with { CaptureTargets = targets.ToArray() };
            return profile with { ChildWindows = children.ToArray() };
        }

        children.Add(new WorkstationChildWindowProfile(
            WindowKey: "notch_profile",
            MustMaximize: true,
            CaptureTargets:
            [
                new WorkstationCaptureTarget("table_scan", tableRoi, ComputeDefaultPagingFocusPoint(tableRoi), pagingActivation, NotchProfilePagingPreparationMode)
            ]
        ));
        return profile with { ChildWindows = children.ToArray() };
    }

    private static WorkstationProfile SetProfileEditorPagingFocus(
        WorkstationProfile profile,
        BBox tableRoi,
        WindowPoint pagingFocus
    )
    {
        var children = (profile.ChildWindows ?? []).ToList();
        var keyNorm = WorkstationProfileKeys.Normalize("profile_editor");
        for (var i = 0; i < children.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(children[i].WindowKey) != keyNorm)
            {
                continue;
            }

            var targets = (children[i].CaptureTargets ?? []).ToList();
            var updated = false;
            for (var j = 0; j < targets.Count; j++)
            {
                if (WorkstationProfileKeys.Normalize(targets[j].Key) != WorkstationProfileKeys.Normalize("table_scan"))
                {
                    continue;
                }

                targets[j] = targets[j] with
                {
                    RoiWindow = targets[j].RoiWindow ?? tableRoi,
                    PagingFocusPointWindow = pagingFocus,
                    PagingPreparationMode = targets[j].PagingPreparationMode ?? NotchProfilePagingPreparationMode
                };
                updated = true;
                break;
            }

            if (!updated)
            {
                targets.Add(new WorkstationCaptureTarget("table_scan", tableRoi, pagingFocus, null, NotchProfilePagingPreparationMode));
            }

            children[i] = children[i] with { CaptureTargets = targets.ToArray() };
            return profile with { ChildWindows = children.ToArray() };
        }

        children.Add(new WorkstationChildWindowProfile(
            WindowKey: "profile_editor",
            MustMaximize: true,
            CaptureTargets:
            [
                new WorkstationCaptureTarget("table_scan", tableRoi, pagingFocus, null, NotchProfilePagingPreparationMode)
            ]
        ));
        return profile with { ChildWindows = children.ToArray() };
    }

    private static WorkstationProfile SetProfileEditorPagingActivation(
        WorkstationProfile profile,
        BBox tableRoi,
        WindowPoint pagingActivation
    )
    {
        var children = (profile.ChildWindows ?? []).ToList();
        var keyNorm = WorkstationProfileKeys.Normalize("profile_editor");
        for (var i = 0; i < children.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(children[i].WindowKey) != keyNorm)
            {
                continue;
            }

            var targets = (children[i].CaptureTargets ?? []).ToList();
            var updated = false;
            for (var j = 0; j < targets.Count; j++)
            {
                if (WorkstationProfileKeys.Normalize(targets[j].Key) != WorkstationProfileKeys.Normalize("table_scan"))
                {
                    continue;
                }

                targets[j] = targets[j] with
                {
                    RoiWindow = targets[j].RoiWindow ?? tableRoi,
                    PagingActivationPointWindow = pagingActivation,
                    PagingPreparationMode = targets[j].PagingPreparationMode ?? NotchProfilePagingPreparationMode
                };
                updated = true;
                break;
            }

            if (!updated)
            {
                targets.Add(new WorkstationCaptureTarget("table_scan", tableRoi, ComputeDefaultPagingFocusPoint(tableRoi), pagingActivation, NotchProfilePagingPreparationMode));
            }

            children[i] = children[i] with { CaptureTargets = targets.ToArray() };
            return profile with { ChildWindows = children.ToArray() };
        }

        children.Add(new WorkstationChildWindowProfile(
            WindowKey: "profile_editor",
            MustMaximize: true,
            CaptureTargets:
            [
                new WorkstationCaptureTarget("table_scan", tableRoi, ComputeDefaultPagingFocusPoint(tableRoi), pagingActivation, NotchProfilePagingPreparationMode)
            ]
        ));
        return profile with { ChildWindows = children.ToArray() };
    }

    private static WorkstationProfile SetProfileEditorTopSignature(
        WorkstationProfile profile,
        BBox verifyRoi,
        string verifySha256
    )
    {
        var children = (profile.ChildWindows ?? []).ToList();
        var keyNorm = WorkstationProfileKeys.Normalize("profile_editor");
        for (var i = 0; i < children.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(children[i].WindowKey) != keyNorm)
            {
                continue;
            }

            var targets = (children[i].VerifyTargets ?? []).ToList();
            var updated = false;
            for (var j = 0; j < targets.Count; j++)
            {
                if (WorkstationProfileKeys.Normalize(targets[j].Key) != WorkstationProfileKeys.Normalize("top_serial"))
                {
                    continue;
                }

                targets[j] = targets[j] with
                {
                    RoiWindow = verifyRoi,
                    Sha256 = verifySha256
                };
                updated = true;
                break;
            }

            if (!updated)
            {
                targets.Add(new WorkstationVerifyTarget("top_serial", verifyRoi, verifySha256));
            }

            children[i] = children[i] with { VerifyTargets = targets.ToArray() };
            return profile with { ChildWindows = children.ToArray() };
        }

        children.Add(new WorkstationChildWindowProfile(
            WindowKey: "profile_editor",
            MustMaximize: true,
            VerifyTargets:
            [
                new WorkstationVerifyTarget("top_serial", verifyRoi, verifySha256)
            ]
        ));
        return profile with { ChildWindows = children.ToArray() };
    }

    private static WorkstationProfile SetDefineNotchProfilesLayoutSignature(
        WorkstationProfile profile,
        BBox verifyRoi,
        string verifySha256
    )
    {
        var children = (profile.ChildWindows ?? []).ToList();
        var keyNorm = WorkstationProfileKeys.Normalize("define_notch_profiles");
        for (var i = 0; i < children.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(children[i].WindowKey) != keyNorm)
            {
                continue;
            }

            var targets = (children[i].VerifyTargets ?? []).ToList();
            var updated = false;
            for (var j = 0; j < targets.Count; j++)
            {
                if (WorkstationProfileKeys.Normalize(targets[j].Key) != WorkstationProfileKeys.Normalize("notch_profiles_layout"))
                {
                    continue;
                }

                targets[j] = targets[j] with
                {
                    RoiWindow = verifyRoi,
                    Sha256 = verifySha256
                };
                updated = true;
                break;
            }

            if (!updated)
            {
                targets.Add(new WorkstationVerifyTarget("notch_profiles_layout", verifyRoi, verifySha256));
            }

            children[i] = children[i] with { VerifyTargets = targets.ToArray() };
            return profile with { ChildWindows = children.ToArray() };
        }

        children.Add(new WorkstationChildWindowProfile(
            WindowKey: "define_notch_profiles",
            MustMaximize: true,
            VerifyTargets:
            [
                new WorkstationVerifyTarget("notch_profiles_layout", verifyRoi, verifySha256)
            ]
        ));
        return profile with { ChildWindows = children.ToArray() };
    }

    private static async Task<bool> PrepareProfileEditorTopStableAsync(
        WindowController controller,
        ScreenCapture capturer,
        TestlabWindowInfo childWindow,
        BBox tableRoi,
        WindowPoint pagingActivation
    )
    {
        const int keyDelayMs = 10;
        const int pagePauseMs = 25;
        const int pgUpCount = 10;
        const int pgUpRetryCount = 5;
        const int stableConsecutive = 2;

        static string CaptureTopHash(ScreenCapture localCapturer, IntPtr hwnd, BBox roi)
        {
            var windowBytes = localCapturer.CaptureWindowPngBytes(hwnd);
            var serialRoi = GetTopSerialVerifyRoiWindow(roi);
            var serialBytes = ImageCropper.TryCropToPngBytes(windowBytes, serialRoi);
            return serialBytes is { Length: > 0 }
                ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(serialBytes))
                : string.Empty;
        }

        void ActivatePagingSurface()
        {
            controller.Activate(childWindow.Hwnd);
            Thread.Sleep(80);
            controller.ClickWindowPoint(childWindow.Hwnd, pagingActivation);
            Thread.Sleep(120);
            controller.Activate(childWindow.Hwnd);
            Thread.Sleep(80);
        }

        void PressPgUpTimes(int times)
        {
            for (var i = 0; i < Math.Max(0, times); i++)
            {
                ActivatePagingSurface();
                controller.PressPageUpToForegroundWindow(keyDelayMs);
            }
        }

        bool ProbeStable()
        {
            var lastHash = CaptureTopHash(capturer, childWindow.Hwnd, tableRoi);
            if (string.IsNullOrWhiteSpace(lastHash))
            {
                return false;
            }

            for (var i = 0; i < stableConsecutive; i++)
            {
                ActivatePagingSurface();
                controller.PressPageUpToForegroundWindow(keyDelayMs);
                Thread.Sleep(pagePauseMs);
                var nextHash = CaptureTopHash(capturer, childWindow.Hwnd, tableRoi);
                if (!string.Equals(lastHash, nextHash, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        PressPgUpTimes(pgUpCount);
        await Task.Delay(pagePauseMs);
        if (ProbeStable())
        {
            return true;
        }

        PressPgUpTimes(pgUpRetryCount);
        await Task.Delay(pagePauseMs);
        return ProbeStable();
    }

    private static BBox GetTopSerialVerifyRoiWindow(BBox tableScanRoiWindow)
    {
        var serialWidth = Math.Clamp((int)Math.Round(tableScanRoiWindow.Width * 0.12), 80, 280);
        var verifyWidth = Math.Clamp(serialWidth + 24, 96, tableScanRoiWindow.Width);
        var verifyHeight = Math.Clamp((int)Math.Round(tableScanRoiWindow.Height * 20 / 100.0), 72, 128);
        verifyHeight = Math.Clamp(verifyHeight, 1, tableScanRoiWindow.Height);
        return new BBox(tableScanRoiWindow.X, tableScanRoiWindow.Y, verifyWidth, verifyHeight);
    }

    private static BBox GetNotchProfilesLayoutVerifyRoiWindow(
        BBox listRoi,
        WindowPoint firstRowAnchor,
        int rowHeight,
        WindowPoint actionClickPoint
    )
    {
        // The left list rows are business data, not stable UI chrome.
        // Item count and labels may change in normal use, so the content-level
        // gate should sign only the bottom control band that stays structurally stable.
        const int horizontalPadding = 12;
        const int bottomPadding = 16;
        const int actionHalfWidth = 56;
        const int actionHalfHeight = 20;
        const int controlBandTopPadding = 42;
        const int listBottomOverlap = 28;
        const int verticalScrollbarExclude = 1;

        var left = Math.Max(0, Math.Min(listRoi.X + 6, actionClickPoint.X - actionHalfWidth) - horizontalPadding);
        var topFromActionBand = actionClickPoint.Y - (actionHalfHeight + controlBandTopPadding);
        var topFromListBottom = listRoi.Y + listRoi.Height - listBottomOverlap;
        var top = Math.Max(0, Math.Min(topFromActionBand, topFromListBottom));
        var right = Math.Max(actionClickPoint.X + actionHalfWidth, listRoi.X + listRoi.Width - verticalScrollbarExclude);
        var bottom = Math.Max(listRoi.Y + listRoi.Height, actionClickPoint.Y + actionHalfHeight) + bottomPadding;
        return new BBox(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static WindowPoint ComputeDefaultPagingFocusPoint(BBox tableRoi)
    {
        var serialWidth = Math.Clamp((int)Math.Round(tableRoi.Width * 0.12), 80, 280);
        var focusX = tableRoi.X + Math.Clamp(serialWidth / 2, 24, Math.Max(24, tableRoi.Width - 1));
        var focusY = tableRoi.Y + Math.Clamp(tableRoi.Height / 3, 24, Math.Max(24, tableRoi.Height - 1));
        return new WindowPoint(focusX, focusY);
    }

    private static string ComputeImageContentSha256Hex(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        if (source.Format != PixelFormats.Bgra32)
        {
            source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        }

        var stride = ((source.PixelWidth * source.Format.BitsPerPixel) + 7) / 8;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true);
        writer.Write(source.PixelWidth);
        writer.Write(source.PixelHeight);
        writer.Write(stride);
        writer.Write(pixels);
        writer.Flush();

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload.ToArray()));
    }

    private sealed class HotkeyListener : IDisposable
    {
        private readonly HwndSource _source;
        private readonly int _id;
        private TaskCompletionSource<bool>? _tcs;

        public HotkeyListener(uint vk)
        {
            _id = 1;
            var parameters = new HwndSourceParameters("CheckMindChildWindowCalibHotkey")
            {
                Width = 0,
                Height = 0,
                WindowStyle = unchecked((int)0x80000000)
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            if (!Win32Native.RegisterHotKey(_source.Handle, _id, Win32Native.MOD_NOREPEAT, vk))
            {
                throw new InvalidOperationException("\u6ce8\u518c\u70ed\u952e\u5931\u8d25\u3002");
            }
        }

        public Task WaitAsync(CancellationToken cancellationToken)
        {
            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));
            return _tcs.Task;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Native.WM_HOTKEY && wParam.ToInt32() == _id)
            {
                handled = true;
                _tcs?.TrySetResult(true);
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            _ = Win32Native.UnregisterHotKey(_source.Handle, _id);
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }
    }
}
