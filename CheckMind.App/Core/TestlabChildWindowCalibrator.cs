using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;

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

    private static BBox CreateRoi(WindowPoint a, WindowPoint b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X, b.X);
        var bottom = Math.Max(a.Y, b.Y);
        return new BBox(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
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

    private static WindowPoint ComputeDefaultPagingFocusPoint(BBox tableRoi)
    {
        var serialWidth = Math.Clamp((int)Math.Round(tableRoi.Width * 0.12), 80, 280);
        var focusX = tableRoi.X + Math.Clamp(serialWidth / 2, 24, Math.Max(24, tableRoi.Width - 1));
        var focusY = tableRoi.Y + Math.Clamp(tableRoi.Height / 3, 24, Math.Max(24, tableRoi.Height - 1));
        return new WindowPoint(focusX, focusY);
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
