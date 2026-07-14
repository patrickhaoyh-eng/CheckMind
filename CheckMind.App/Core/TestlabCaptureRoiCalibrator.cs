using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace CheckMind.App.Core;

public sealed class TestlabCaptureRoiCalibrator
{
    public async Task CalibrateAsync(RunContext run, string[] tabs)
    {
        if (tabs.Length == 0)
        {
            throw new InvalidOperationException("未配置需要标定截图框的页签列表。");
        }

        var locator = new TestlabWindowLocator();
        var controller = new WindowController();
        var capturer = new ScreenCapture();
        var screenshots = new ScreenshotStore();
        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            throw new InvalidOperationException($"未找到 profile：{store.ProfilePath}。请先完成基础标定。");
        }

        var profile = store.Load();
        using var hotkey = new HotkeyListener(Win32Native.VK_F8);

        _ = System.Windows.MessageBox.Show(
            "截图框标定模式：每个截图框都需要记录左上角和右下角，均使用 F8 取点。标定过程中请保持 Testlab 在前台。",
            "CheckMind - 标定截图框",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        foreach (var tab in tabs)
        {
            if (string.IsNullOrWhiteSpace(tab))
            {
                continue;
            }

            var click = profile.FindTabClickTarget(tab)?.ClickPoint
                ?? throw new InvalidOperationException($"缺少页签 ClickPoint：{tab}。请先完成页签点击点标定。");

            var win = locator.Find();
            controller.Maximize(win.Hwnd);
            await Task.Delay(250);
            controller.Activate(win.Hwnd);
            await Task.Delay(100);
            controller.ClickScreenPoint(win.Rect.Left + click.X, win.Rect.Top + click.Y);
            await Task.Delay(300);

            foreach (var target in GetCaptureTargetsForTab(tab))
            {
                var topLeft = await CapturePointAsync(locator, controller, hotkey, tab, target.Label, "左上角");
                var bottomRight = await CapturePointAsync(locator, controller, hotkey, tab, target.Label, "右下角");
                var roi = CreateRoi(topLeft, bottomRight);

                win = locator.Find();
                var windowBytes = capturer.CaptureWindowPngBytes(win.Hwnd);
                var roiBytes = ImageCropper.TryCropToPngBytes(windowBytes, roi);
                if (roiBytes is not null)
                {
                    _ = screenshots.SaveDebugPng(run, $"calib_{NormalizeKey(tab)}_{target.Key}", roiBytes);
                }

                profile = SetCaptureTarget(profile, tab, target.Key, roi, tabs);
                if (NormalizeKey(target.Key) == NormalizeKey("table_scan"))
                {
                    var topSerialVerify = await TryComputeTopSerialVerifyTargetAfterResetAsync(
                        locator,
                        controller,
                        capturer,
                        tab,
                        roi
                    );
                    if (topSerialVerify is not null)
                    {
                        var (verifyRoi, verifySha256, verifyBytes) = topSerialVerify.Value;
                        _ = screenshots.SaveDebugPng(run, $"calib_{NormalizeKey(tab)}_top_serial", verifyBytes);
                        profile = SetTopSerialVerifyTarget(profile, tab, verifyRoi, verifySha256, tabs);
                    }
                }
                store.Save(profile);

                TestlabDebugMarkers.WritePhase(
                    "calib.capture_roi_written",
                    run.RunDirectory,
                    $"tab={tab};key={target.Key};roi=({roi.X},{roi.Y},{roi.Width},{roi.Height});profile={store.ProfilePath}"
                );
            }
        }

        _ = System.Windows.MessageBox.Show(
            $"截图框标定完成。{Environment.NewLine}{store.ProfilePath}",
            "CheckMind - 标定完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private static async Task<WindowPoint> CapturePointAsync(
        TestlabWindowLocator locator,
        WindowController controller,
        HotkeyListener hotkey,
        string tab,
        string captureLabel,
        string cornerLabel)
    {
        _ = System.Windows.MessageBox.Show(
            $"请将鼠标移动到 [{tab}] 的 [{captureLabel}] {cornerLabel}，然后按 F8。",
            "CheckMind - 标定截图框",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        await hotkey.WaitAsync(CancellationToken.None);

        var cursor = controller.GetCursorScreenPoint();
        if (cursor is null)
        {
            throw new InvalidOperationException("读取鼠标位置失败。");
        }

        var win = locator.Find();
        controller.Activate(win.Hwnd);
        await Task.Delay(50);

        var localX = cursor.Value.X - win.Rect.Left;
        var localY = cursor.Value.Y - win.Rect.Top;
        if (localX < 0 || localY < 0 || localX >= win.Rect.Width || localY >= win.Rect.Height)
        {
            throw new InvalidOperationException($"标定点不在 Testlab 窗口内：screen=({cursor.Value.X},{cursor.Value.Y}) win=({win.Rect.Left},{win.Rect.Top},{win.Rect.Width},{win.Rect.Height})");
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

    private static IEnumerable<(string Key, string Label)> GetCaptureTargetsForTab(string tab)
    {
        yield return ("table_entry", "表格入口截图框");
        yield return ("table_scan", "表格滑窗截图框");
    }

    private static WorkstationProfile SetCaptureTarget(WorkstationProfile profile, string tabName, string targetKey, BBox roi, string[] tabs)
    {
        var pages = (profile.Pages ?? [])
            .ToList();

        if (pages.Count == 0)
        {
            foreach (var t in tabs)
            {
                if (!string.IsNullOrWhiteSpace(t))
                {
                    pages.Add(new WorkstationPageProfile(t));
                }
            }
        }

        var targetNorm = WorkstationProfileKeys.Normalize(tabName);
        var keyNorm = WorkstationProfileKeys.Normalize(targetKey);
        var updated = false;
        for (var i = 0; i < pages.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(pages[i].TabName) != targetNorm)
            {
                continue;
            }

            var targets = (pages[i].CaptureTargets ?? [])
                .ToList();
            var targetUpdated = false;
            for (var j = 0; j < targets.Count; j++)
            {
                if (WorkstationProfileKeys.Normalize(targets[j].Key) == keyNorm)
                {
                    targets[j] = targets[j] with { RoiWindow = roi };
                    targetUpdated = true;
                    break;
                }
            }

            if (!targetUpdated)
            {
                targets.Add(new WorkstationCaptureTarget(targetKey, roi));
            }

            var page = pages[i] with
            {
                CaptureRoiWindow = null,
                CaptureTargets = targets.ToArray()
            };
            pages[i] = page;
            updated = true;
            break;
        }

        if (!updated)
        {
            var page = new WorkstationPageProfile(
                TabName: tabName,
                CaptureTargets: [new WorkstationCaptureTarget(targetKey, roi)]
            );
            pages.Add(page);
        }

        return profile with { Pages = pages.ToArray() };
    }

    private static WorkstationProfile SetTopSerialVerifyTarget(WorkstationProfile profile, string tabName, BBox roi, string sha256, string[] tabs)
    {
        var pages = (profile.Pages ?? [])
            .ToList();

        if (pages.Count == 0)
        {
            foreach (var t in tabs)
            {
                if (!string.IsNullOrWhiteSpace(t))
                {
                    pages.Add(new WorkstationPageProfile(t));
                }
            }
        }

        var targetNorm = WorkstationProfileKeys.Normalize(tabName);
        var updated = false;
        for (var i = 0; i < pages.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(pages[i].TabName) != targetNorm)
            {
                continue;
            }

            var verifyTargets = (pages[i].VerifyTargets ?? [])
                .ToList();
            var targetUpdated = false;
            for (var j = 0; j < verifyTargets.Count; j++)
            {
                if (WorkstationProfileKeys.Normalize(verifyTargets[j].Key) == WorkstationProfileKeys.Normalize("top_serial"))
                {
                    verifyTargets[j] = verifyTargets[j] with { RoiWindow = roi, Sha256 = sha256 };
                    targetUpdated = true;
                    break;
                }
            }

            if (!targetUpdated)
            {
                verifyTargets.Add(new WorkstationVerifyTarget("top_serial", roi, sha256));
            }

            pages[i] = pages[i] with
            {
                TopSerialVerifySha256 = null,
                VerifyTargets = verifyTargets.ToArray()
            };
            updated = true;
            break;
        }

        if (!updated)
        {
            pages.Add(new WorkstationPageProfile(
                TabName: tabName,
                VerifyTargets: [new WorkstationVerifyTarget("top_serial", roi, sha256)]
            ));
        }

        return profile with { Pages = pages.ToArray() };
    }

    private static async Task<(BBox RoiWindow, string Sha256, byte[] Bytes)?> TryComputeTopSerialVerifyTargetAfterResetAsync(
        TestlabWindowLocator locator,
        WindowController controller,
        ScreenCapture capturer,
        string tabName,
        BBox tableScanRoiWindow)
    {
        var win = locator.Find();
        controller.Activate(win.Hwnd);
        await Task.Delay(80);

        var serialWidth = Math.Clamp((int)Math.Round(tableScanRoiWindow.Width * 0.12), 80, 280);
        var serialRoiWindow = new BBox(tableScanRoiWindow.X, tableScanRoiWindow.Y, serialWidth, tableScanRoiWindow.Height);
        var scrollbarWidth = Math.Min(18, tableScanRoiWindow.Width);
        var scrollbarRoiWindow = new BBox(
            tableScanRoiWindow.X + Math.Max(0, tableScanRoiWindow.Width - scrollbarWidth),
            tableScanRoiWindow.Y,
            scrollbarWidth,
            tableScanRoiWindow.Height
        );

        var focusX = win.Rect.Left + tableScanRoiWindow.X + Math.Clamp(serialWidth / 2, 24, Math.Max(24, tableScanRoiWindow.Width - 1));
        var focusY = win.Rect.Top + tableScanRoiWindow.Y + Math.Clamp(tableScanRoiWindow.Height / 3, 24, Math.Max(24, tableScanRoiWindow.Height - 1));
        controller.ClickScreenPoint(focusX, focusY);
        await Task.Delay(60);
        controller.ClickScreenPoint(focusX, focusY);
        await Task.Delay(60);

        var keyDelayMs = GetIntEnv("CHECKMIND_TABLE_KEY_DELAY_MS", 10);
        var pagePauseMs = GetIntEnv("CHECKMIND_TABLE_PAGE_PAUSE_MS", 25);
        var pgUpCount = GetIntEnv("CHECKMIND_TABLE_RESET_TOP_PGUP_COUNT", 10);
        var pgUpRetryCount = GetIntEnv("CHECKMIND_TABLE_RESET_TOP_PGUP_RETRY_COUNT", 5);
        var stableConsecutive = Math.Max(1, GetIntEnv("CHECKMIND_TABLE_RESET_TOP_STABLE_CONSECUTIVE", 2));

        static string CaptureStateSha(byte[] windowBytes, BBox serialRoiWindow, BBox scrollbarRoiWindow)
        {
            var serialBytes = ImageCropper.TryCropToPngBytes(windowBytes, serialRoiWindow) ?? windowBytes;
            var scrollbarBytes = ImageCropper.TryCropToPngBytes(windowBytes, scrollbarRoiWindow) ?? windowBytes;
            return $"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(serialBytes))}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(scrollbarBytes))}";
        }

        async Task<byte[]> ProbeTopStableAsync()
        {
            var probeWindowBytes0 = capturer.CaptureWindowPngBytes(win.Hwnd);
            var lastHash = CaptureStateSha(probeWindowBytes0, serialRoiWindow, scrollbarRoiWindow);
            var currentWindowBytes = probeWindowBytes0;
            var allSame = true;

            for (var i = 0; i < stableConsecutive; i++)
            {
                controller.PressPageUp(keyDelayMs);
                await Task.Delay(pagePauseMs);
                var probeWindowBytes = capturer.CaptureWindowPngBytes(win.Hwnd);
                var nextHash = CaptureStateSha(probeWindowBytes, serialRoiWindow, scrollbarRoiWindow);
                allSame = allSame && string.Equals(lastHash, nextHash, StringComparison.OrdinalIgnoreCase);
                lastHash = nextHash;
                currentWindowBytes = probeWindowBytes;
            }

            return allSame ? currentWindowBytes : Array.Empty<byte>();
        }

        void PressPgUpTimes(int times)
        {
            for (var i = 0; i < Math.Max(0, times); i++)
            {
                controller.PressPageUp(keyDelayMs);
            }
        }

        PressPgUpTimes(pgUpCount);
        await Task.Delay(pagePauseMs);
        var topWindowBytes = await ProbeTopStableAsync();
        if (topWindowBytes.Length == 0)
        {
            PressPgUpTimes(pgUpRetryCount);
            await Task.Delay(pagePauseMs);
            topWindowBytes = await ProbeTopStableAsync();
        }

        if (topWindowBytes.Length == 0)
        {
            topWindowBytes = capturer.CaptureWindowPngBytes(win.Hwnd);
        }

        var verifyRoi = GetTopSerialVerifyRoiWindow(tabName, tableScanRoiWindow);
        var verifyBytes = ImageCropper.TryCropToPngBytes(topWindowBytes, verifyRoi);
        if (verifyBytes is null || verifyBytes.Length == 0)
        {
            return null;
        }

        var verifySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(verifyBytes));
        return (verifyRoi, verifySha256, verifyBytes);
    }

    private static BBox GetTopSerialVerifyRoiWindow(string tabName, BBox tableScanRoiWindow)
    {
        var serialWidth = Math.Clamp((int)Math.Round(tableScanRoiWindow.Width * 0.12), 80, 280);
        var verifyWidth = Math.Clamp(serialWidth + 24, 96, tableScanRoiWindow.Width);
        var normalizedTab = NormalizeKey(tabName);
        var verifyHeight = normalizedTab == NormalizeKey("Sine Setup")
            ? Math.Clamp((int)Math.Round(tableScanRoiWindow.Height * 0.28), 96, 160)
            : Math.Clamp((int)Math.Round(tableScanRoiWindow.Height * 20 / 100.0), 72, 128);
        verifyHeight = Math.Clamp(verifyHeight, 1, tableScanRoiWindow.Height);
        return new BBox(tableScanRoiWindow.X, tableScanRoiWindow.Y, verifyWidth, verifyHeight);
    }

    private static int GetIntEnv(string key, int defaultValue)
    {
        var raw = (Environment.GetEnvironmentVariable(key) ?? "").Trim();
        return int.TryParse(raw, out var value) ? value : defaultValue;
    }

    private static string NormalizeKey(string value)
        => WorkstationProfileKeys.Normalize(value);

    private sealed class HotkeyListener : IDisposable
    {
        private readonly HwndSource _source;
        private readonly int _id;
        private TaskCompletionSource<bool>? _tcs;

        public HotkeyListener(uint vk)
        {
            _id = 1;
            var parameters = new HwndSourceParameters("CheckMindCaptureRoiHotkey")
            {
                Width = 0,
                Height = 0,
                WindowStyle = unchecked((int)0x80000000)
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            if (!Win32Native.RegisterHotKey(_source.Handle, _id, Win32Native.MOD_NOREPEAT, vk))
            {
                throw new InvalidOperationException("RegisterHotKey 失败。");
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
