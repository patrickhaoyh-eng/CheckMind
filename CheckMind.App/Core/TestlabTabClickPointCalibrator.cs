using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CheckMind.App.Core;

public sealed class TestlabTabClickPointCalibrator
{
    private const int BaselineWidth = 1920;
    private const int BaselineHeight = 1080;
    private const int BaselineDpiScalePercent = 100;

    public async Task CalibrateAsync(RunContext run, string[] tabs)
    {
        if (tabs.Length == 0)
        {
            throw new InvalidOperationException("未配置需要标定的页签列表。");
        }

        var locator = new TestlabWindowLocator();
        var controller = new WindowController();
        var capturer = new ScreenCapture();
        var screenshots = new ScreenshotStore();
        var store = WorkstationProfileStore.CreateDefault();
        var reuseRaw = (Environment.GetEnvironmentVariable("CHECKMIND_CALIBRATE_REUSE_EXISTING") ?? "").Trim();
        var reuseExisting = string.Equals(reuseRaw, "1", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(reuseRaw, "true", StringComparison.OrdinalIgnoreCase);

        var win = locator.Find();
        controller.Maximize(win.Hwnd);
        await Task.Delay(300);
        controller.Activate(win.Hwnd);
        await Task.Delay(150);

        var probe = new WorkstationEnvironmentProbe();
        var measured = probe.Probe(win.Hwnd);

        var profile = File.Exists(store.ProfilePath)
            ? store.Load()
            : CreateProfileTemplate(measured, tabs);

        Directory.CreateDirectory(Path.GetDirectoryName(store.ProfilePath)!);
        File.WriteAllText(store.ProfilePath, System.Text.Json.JsonSerializer.Serialize(profile, JsonOptions.Default), new UTF8Encoding(false));

        using var hotkey = reuseExisting ? null : new HotkeyListener(Win32Native.VK_F8);

        if (!reuseExisting)
        {
            _ = System.Windows.MessageBox.Show(
                "标定模式：每个页签请把鼠标移动到页签可点击区域的中心位置，然后按 F8 记录。标定过程中请保持 Testlab 在前台。",
                "CheckMind - 标定 Tab ClickPoint",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        var stableConsecutive = Math.Clamp(GetIntEnv("CHECKMIND_CALIBRATE_VERIFY_STABLE_CONSECUTIVE", 2), 1, 4);
        var sampleCount = Math.Clamp(GetIntEnv("CHECKMIND_CALIBRATE_VERIFY_SAMPLE_COUNT", 4), stableConsecutive, 8);
        var sampleSleepMs = Math.Clamp(GetIntEnv("CHECKMIND_CALIBRATE_VERIFY_SAMPLE_SLEEP_MS", 80), 0, 500);

        string? lastVerifySha = null;
        foreach (var tab in tabs)
        {
            if (string.IsNullOrWhiteSpace(tab))
            {
                continue;
            }

            win = locator.Find();
            TestlabDebugMarkers.WritePhase("calib.wait_hotkey", run.RunDirectory, $"tab={tab};profile={store.ProfilePath}");
            var existing = profile.FindTabClickTarget(tab)?.ClickPoint;
            WindowPoint point;
            if (reuseExisting && existing is WindowPoint ep)
            {
                point = ep;
            }
            else
            {
                _ = System.Windows.MessageBox.Show(
                    $"请将鼠标移动到页签 [{tab}] 的可点击中心，然后按 F8。",
                    "CheckMind - 标定 Tab ClickPoint",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                await hotkey!.WaitAsync(CancellationToken.None);

                var cursor = controller.GetCursorScreenPoint();
                if (cursor is null)
                {
                    throw new InvalidOperationException("读取鼠标位置失败。");
                }

                win = locator.Find();
                controller.Activate(win.Hwnd);
                await Task.Delay(50);

                var localX = cursor.Value.X - win.Rect.Left;
                var localY = cursor.Value.Y - win.Rect.Top;
                if (localX < 0 || localY < 0 || localX >= win.Rect.Width || localY >= win.Rect.Height)
                {
                    throw new InvalidOperationException($"标定点不在 Testlab 窗口内：screen=({cursor.Value.X},{cursor.Value.Y}) win=({win.Rect.Left},{win.Rect.Top},{win.Rect.Width},{win.Rect.Height})");
                }

                point = new WindowPoint(localX, localY);
                profile = SetTabClickPoint(profile, tab, point, tabs);
                File.WriteAllText(store.ProfilePath, System.Text.Json.JsonSerializer.Serialize(profile, JsonOptions.Default), new UTF8Encoding(false));
            }

            TestlabDebugMarkers.WritePhase(
                "calib.captured",
                run.RunDirectory,
                $"tab={tab};win=({win.Rect.Left},{win.Rect.Top});local=({point.X},{point.Y});profile={store.ProfilePath}"
            );

            byte[]? windowBytes = null;
            string? windowPath = null;
            (BBox RoiWindow, string Sha256)? verify = null;
            var suspiciousSame = false;
            var signatureSettled = false;
            for (var clickTry = 0; clickTry < 3; clickTry++)
            {
                win = locator.Find();
                controller.Activate(win.Hwnd);
                await Task.Delay(30);
                controller.ClickScreenPoint(win.Rect.Left + point.X, win.Rect.Top + point.Y);
                await Task.Delay(220 + (clickTry * 120));

                verify = null;
                windowBytes = null;
                windowPath = null;
                suspiciousSame = false;

                string? lastSampleSha = null;
                var stableRun = 0;
                for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    windowBytes = capturer.CaptureWindowPngBytes(win.Hwnd);
                    windowPath = screenshots.SaveDebugPng(run, $"calib_{NormalizeKey(tab)}_window_try_{clickTry:00}_sample_{sampleIndex:00}", windowBytes);
                    verify = TryComputeVerifySignature(tab, windowBytes, point);
                    if (verify is null)
                    {
                        stableRun = 0;
                    }
                    else
                    {
                        var (roiProbe, shaProbe) = verify.Value;
                        var roiProbeBytes = ImageCropper.TryCropToPngBytes(windowBytes, roiProbe);
                        if (roiProbeBytes is not null)
                        {
                                _ = screenshots.SaveDebugPng(run, $"calib_{NormalizeKey(tab)}_verify_roi_try_{clickTry:00}_{sampleIndex:00}", roiProbeBytes);
                        }

                        if (string.Equals(lastSampleSha, shaProbe, StringComparison.OrdinalIgnoreCase))
                        {
                            stableRun++;
                        }
                        else
                        {
                            stableRun = 1;
                        }

                        lastSampleSha = shaProbe;
                        if (stableRun >= stableConsecutive)
                        {
                            suspiciousSame = !string.IsNullOrWhiteSpace(lastVerifySha) &&
                                             string.Equals(lastVerifySha, shaProbe, StringComparison.OrdinalIgnoreCase);
                            signatureSettled = !suspiciousSame;
                            if (!suspiciousSame)
                            {
                                break;
                            }
                        }
                    }

                    if ((sampleIndex + 1) < sampleCount)
                    {
                        await Task.Delay(sampleSleepMs);
                    }
                }

                if (verify is null)
                {
                    TestlabDebugMarkers.WritePhase(
                        "calib.verify_signature_retry",
                        run.RunDirectory,
                        $"tab={tab};clickTry={clickTry};reason=empty_capture"
                    );
                    continue;
                }

                var (roiProbeFinal, shaProbeFinal) = verify.Value;
                if (!signatureSettled)
                {
                    TestlabDebugMarkers.WritePhase(
                        "calib.verify_signature_retry",
                        run.RunDirectory,
                        $"tab={tab};clickTry={clickTry};sha={shaProbeFinal};reason=unstable;stableConsecutive={stableConsecutive};sampleCount={sampleCount};sampleSleepMs={sampleSleepMs};winShot={windowPath}"
                    );
                    continue;
                }

                break;
            }

            if (verify is not null && signatureSettled)
            {
                var (roi, sha) = verify.Value;
                if (suspiciousSame)
                {
                    TestlabDebugMarkers.WritePhase("calib.verify_signature_suspicious_same", run.RunDirectory, $"tab={tab};sha={sha};winShot={windowPath}");
                    throw new InvalidOperationException("验真签名疑似未切页（两次签名相同）。请确认 Testlab 已切到对应页签后重试。");
                }
                lastVerifySha = sha;

                if (windowBytes is null)
                {
                    throw new InvalidOperationException("标定时截图为空，无法生成验真签名。");
                }

                var roiBytes = ImageCropper.TryCropToPngBytes(windowBytes, roi);
                if (roiBytes is not null)
                {
                    _ = screenshots.SaveDebugPng(run, $"calib_{NormalizeKey(tab)}_verify_roi", roiBytes);
                    _ = screenshots.SaveDebugPng(run, $"calib_{NormalizeKey(tab)}_verify_roi_try", roiBytes);
                }
                profile = SetVerifySignature(profile, tab, roi, sha, tabs);
                File.WriteAllText(store.ProfilePath, System.Text.Json.JsonSerializer.Serialize(profile, JsonOptions.Default), new UTF8Encoding(false));
                var payloadPath = Path.Combine(run.RunDirectory, $"calib_{NormalizeKey(tab)}_verify.json");
                File.WriteAllText(
                    payloadPath,
                    System.Text.Json.JsonSerializer.Serialize(
                        new { tabName = tab, verifyRoiWindow = roi, verifySha256 = sha, windowPath },
                        JsonOptions.Default
                    ),
                    new UTF8Encoding(false)
                );
                TestlabDebugMarkers.WritePhase("calib.verify_signature_written", run.RunDirectory, $"tab={tab};roi=({roi.X},{roi.Y},{roi.Width},{roi.Height});sha={sha}");
            }
            else
            {
                throw new InvalidOperationException($"验真签名未稳定，无法完成 [{tab}] 标定。请保持 Testlab 前台并重试。");
            }

            if (!reuseExisting)
            {
                _ = System.Windows.MessageBox.Show(
                    $"已更新 [{tab}] ClickPoint=({point.X},{point.Y}) 并写入 profile。\r\n{store.ProfilePath}",
                    "CheckMind - 标定完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }

        TestlabDebugMarkers.WritePhase("calib.done", run.RunDirectory, $"profile={store.ProfilePath}");
    }

    private static WorkstationProfile CreateProfileTemplate(WorkstationMeasuredEnvironment measured, string[] tabs)
    {
        return new WorkstationProfile(
            new WorkstationProfileEnvironment(
                TargetMonitorIndex: 0,
                TargetWidth: BaselineWidth,
                TargetHeight: BaselineHeight,
                DpiScalePercent: BaselineDpiScalePercent
            ),
            new WorkstationProfileWindow(
                MustBeMaximized: true,
                WindowRectScreen: new BBox(0, 0, BaselineWidth, BaselineHeight)
            ),
            new WorkstationProfileTolerances(
                PixelTolerance: 4
            ),
            new WorkstationProfileNavigation(
                TabClickPoints: tabs
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => new WorkstationTabClickTarget(t))
                    .ToArray()
            ),
            [],
            tabs
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(CreatePageProfileTemplate)
                .ToArray()
        );
    }

    private static WorkstationPageProfile CreatePageProfileTemplate(string tabName)
        => new(
            TabName: tabName,
            CaptureTargets:
            [
                new WorkstationCaptureTarget("table_entry"),
                new WorkstationCaptureTarget("table_scan")
            ],
            VerifyTargets:
            [
                new WorkstationVerifyTarget("tab_verify"),
                new WorkstationVerifyTarget("top_serial")
            ]
        );

    private static WorkstationProfile SetTabClickPoint(WorkstationProfile profile, string tabName, WindowPoint point, string[] tabs)
    {
        var nav = profile.Navigation ?? new WorkstationProfileNavigation([]);
        var list = (nav.TabClickPoints ?? [])
            .ToList();

        if (list.Count == 0)
        {
            foreach (var t in tabs)
            {
                if (!string.IsNullOrWhiteSpace(t))
                {
                    list.Add(new WorkstationTabClickTarget(t));
                }
            }
        }

        var targetNorm = NormalizeKey(tabName);
        var updated = false;
        for (var i = 0; i < list.Count; i++)
        {
            var nameNorm = NormalizeKey(list[i].TabName);
            if (nameNorm == targetNorm)
            {
                list[i] = new WorkstationTabClickTarget(list[i].TabName, point);
                updated = true;
                break;
            }
        }

        if (!updated)
        {
            list.Add(new WorkstationTabClickTarget(tabName, point));
        }

        return profile with
        {
            Navigation = nav with { TabClickPoints = list.ToArray() }
        };
    }

    private static WorkstationProfile SetVerifySignature(WorkstationProfile profile, string tabName, BBox roi, string sha256, string[] tabs)
    {
        var pages = (profile.Pages ?? [])
            .ToList();

        if (pages.Count == 0)
        {
            foreach (var t in tabs)
            {
                if (!string.IsNullOrWhiteSpace(t))
                {
                    pages.Add(CreatePageProfileTemplate(t));
                }
            }
        }

        var targetNorm = NormalizeKey(tabName);
        var updated = false;
        for (var i = 0; i < pages.Count; i++)
        {
            if (NormalizeKey(pages[i].TabName) == targetNorm)
            {
                var verifyTargets = (pages[i].VerifyTargets ?? [])
                    .ToList();
                var tabVerifyUpdated = false;
                for (var j = 0; j < verifyTargets.Count; j++)
                {
                    if (NormalizeKey(verifyTargets[j].Key) == NormalizeKey("tab_verify"))
                    {
                        verifyTargets[j] = verifyTargets[j] with { RoiWindow = roi, Sha256 = sha256 };
                        tabVerifyUpdated = true;
                        break;
                    }
                }

                if (!tabVerifyUpdated)
                {
                    verifyTargets.Add(new WorkstationVerifyTarget("tab_verify", roi, sha256));
                }

                pages[i] = pages[i] with
                {
                    VerifyRoiWindow = roi,
                    VerifySha256 = sha256,
                    VerifyTargets = verifyTargets.ToArray()
                };
                updated = true;
                break;
            }
        }

        if (!updated)
        {
            var template = CreatePageProfileTemplate(tabName);
            var verifyTargets = (template.VerifyTargets ?? [])
                .ToList();
            var tabVerifyUpdated = false;
            for (var j = 0; j < verifyTargets.Count; j++)
            {
                if (NormalizeKey(verifyTargets[j].Key) == NormalizeKey("tab_verify"))
                {
                    verifyTargets[j] = verifyTargets[j] with { RoiWindow = roi, Sha256 = sha256 };
                    tabVerifyUpdated = true;
                    break;
                }
            }

            if (!tabVerifyUpdated)
            {
                verifyTargets.Add(new WorkstationVerifyTarget("tab_verify", roi, sha256));
            }

            pages.Add(template with
            {
                VerifyRoiWindow = roi,
                VerifySha256 = sha256,
                VerifyTargets = verifyTargets.ToArray()
            });
        }

        return profile with { Pages = pages.ToArray() };
    }

    private static (BBox RoiWindow, string Sha256)? TryComputeVerifySignature(string tabName, byte[] windowPngBytes, WindowPoint clickPointWindow)
    {
        var (w, h) = ImageGeometry.GetSize(windowPngBytes);
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        var normalizedTab = NormalizeKey(tabName);
        BBox roi;
        if (normalizedTab == NormalizeKey("Channel Setup") || normalizedTab == NormalizeKey("Sine Setup"))
        {
            // These pages are best distinguished by the actual blue title strip below the toolbar,
            // not by the top chrome or the bottom worksheet tabs.
            var x = 0;
            var y = Math.Clamp((int)Math.Round(h * 0.065), 72, Math.Max(0, h - 1));
            var roiWidth = Math.Clamp((int)Math.Round(w * 0.18), 300, Math.Min(520, w));
            var roiHeight = Math.Clamp((int)Math.Round(h * 0.040), 48, Math.Min(96, h));
            roiWidth = Math.Clamp(roiWidth, 1, Math.Max(1, w - x));
            roiHeight = Math.Clamp(roiHeight, 1, Math.Max(1, h - y));
            roi = new BBox(x, y, roiWidth, roiHeight);
        }
        else
        {
            var roiWidth = Math.Clamp((int)Math.Round(w * 0.045), 90, Math.Min(160, w));
            var roiHeight = Math.Clamp((int)Math.Round(h * 0.022), 28, Math.Min(48, h));
            var x = Math.Clamp(clickPointWindow.X - (roiWidth / 2), 0, Math.Max(0, w - 1));
            var y = Math.Clamp(clickPointWindow.Y - (roiHeight / 2), 0, Math.Max(0, h - 1));
            roiWidth = Math.Clamp(roiWidth, 1, Math.Max(1, w - x));
            roiHeight = Math.Clamp(roiHeight, 1, Math.Max(1, h - y));
            roi = new BBox(x, y, roiWidth, roiHeight);
        }

        var roiBytes = ImageCropper.TryCropToPngBytes(windowPngBytes, roi);
        if (roiBytes is null)
        {
            return null;
        }

        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(roiBytes));
        return (roi, sha);
    }

    private static int GetIntEnv(string key, int defaultValue)
    {
        var raw = (Environment.GetEnvironmentVariable(key) ?? "").Trim();
        return int.TryParse(raw, out var value) ? value : defaultValue;
    }

    private static string NormalizeKey(string value)
    {
        var chars = value.Where(ch => !char.IsWhiteSpace(ch) && ch is not ('-' or '_' or ':' or '：'))
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }


    private sealed class HotkeyListener : IDisposable
    {
        private readonly HwndSource _source;
        private readonly int _id;
        private TaskCompletionSource<bool>? _tcs;

        public HotkeyListener(uint vk)
        {
            _id = 1;
            var parameters = new HwndSourceParameters("CheckMindHotkey")
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
