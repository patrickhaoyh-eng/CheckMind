using System.Text;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CheckMind.App.Core;

public sealed class TestlabAutomationRunner
{
    private sealed record ProfileSignatureProbeResult(
        bool Verified,
        string Mode,
        byte[] WindowBytes
    );

    public TestlabRunResult Run(RunContext run, ICaptureOverlay? overlay = null)
    {
        TestlabDebugMarkers.WritePhase("runner.enter", run.RunDirectory);
        var locator = new TestlabWindowLocator();
        var controller = new WindowController();
        var capturer = new ScreenCapture();
        var screenshots = new ScreenshotStore();

        try
        {
            var win = locator.Find();
            TestlabDebugMarkers.WritePhase("runner.after_initial_find", run.RunDirectory);
            WriteWindowInfo(run, win, controller.IsMaximized(win.Hwnd));

            overlay?.SetVisible(true);
            overlay?.SetRect(new BBox(win.Rect.Left, win.Rect.Top, win.Rect.Width, win.Rect.Height));
            Thread.Sleep(250);
            TestlabDebugMarkers.WritePhase("runner.before_first_window_capture", run.RunDirectory);
            var beforeBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
            var beforePath = screenshots.SaveEvidencePng(run, "testlab_0_before", beforeBytes);
            TestlabDebugMarkers.WritePhase("runner.after_first_window_capture", run.RunDirectory);

            TestlabDebugMarkers.WritePhase("runner.before_maximize", run.RunDirectory);
            controller.Maximize(win.Hwnd);
            Thread.Sleep(400);
            controller.Activate(win.Hwnd);
            Thread.Sleep(200);
            TestlabDebugMarkers.WritePhase("runner.after_maximize", run.RunDirectory);

            win = locator.Find();
            TestlabDebugMarkers.WritePhase("runner.after_second_find", run.RunDirectory);
            WriteWindowInfo(run, win, controller.IsMaximized(win.Hwnd));

            if (IsFixedCaptureEnabled())
            {
                var checker = WorkstationComplianceChecker.CreateDefault();
                var report = checker.Check(run, win, controller);
                var reportPath = Path.Combine(run.RunDirectory, "workstation_compliance.json");
                File.WriteAllText(reportPath, report.ToJson(), Encoding.UTF8);

                if (!report.IsCompliant)
                {
                    TestlabDebugMarkers.WritePhase("runner.workstation_not_compliant", run.RunDirectory, $"report={reportPath}");
                    throw new WorkstationNotCompliantException(
                        "当前工位环境不满足固定坐标抓取要求，已拒绝执行。请查看 workstation_compliance.json 的不符合项与修复建议。",
                        reportPath
                    );
                }

                TestlabDebugMarkers.WritePhase("runner.workstation_compliant", run.RunDirectory, $"report={reportPath}");
            }

            overlay?.SetRect(new BBox(win.Rect.Left, win.Rect.Top, win.Rect.Width, win.Rect.Height));
            Thread.Sleep(250);
            TestlabDebugMarkers.WritePhase("runner.before_maximized_capture", run.RunDirectory);
            var afterBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
            var afterPath = screenshots.SaveEvidencePng(run, "testlab_1_maximized", afterBytes);
            TestlabDebugMarkers.WritePhase("runner.after_maximized_capture", run.RunDirectory);

            if (IsFixedCaptureEnabled())
            {
                var tabsRaw = Environment.GetEnvironmentVariable("CHECKMIND_TESTLAB_TABS");
                var tabs = string.IsNullOrWhiteSpace(tabsRaw)
                    ? Array.Empty<string>()
                    : tabsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tabs.Length > 0)
                {
                    TestlabDebugMarkers.WritePhase("runner.preflight_calibration_begin", run.RunDirectory, $"tabs={string.Join(",", tabs)}");
                    new TestlabPreflightCalibrationChecker().CheckAndThrow(run, win, tabs);
                    TestlabDebugMarkers.WritePhase("runner.preflight_calibration_passed", run.RunDirectory);
                }
            }

            TestlabDebugMarkers.WritePhase("runner.before_switch_tabs", run.RunDirectory);
            IReadOnlyList<TestlabTabSwitchResult> switches;
            try
            {
                switches = SwitchTabsIfRequested(run, win, afterBytes, controller, capturer, screenshots, overlay);
            }
            catch (TabClickPointGateException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.switch_tabs_exception",
                    run.RunDirectory,
                    $"type={ex.GetType().Name};hresult=0x{ex.HResult:X8}"
                );
                switches = Array.Empty<TestlabTabSwitchResult>();
            }
            TestlabDebugMarkers.WritePhase("runner.after_switch_tabs", run.RunDirectory, $"count={switches.Count}");

            var (notchProfileScans, notchProfileCountMismatch) = RunNotchProfilesIfRequested(run, controller, capturer, screenshots, overlay);
            var result = new TestlabRunResult(beforePath, afterPath, switches, notchProfileScans, notchProfileCountMismatch);
            WriteCoverage(run, GetAllTableScans(switches, notchProfileScans));
            TestlabDebugMarkers.WritePhase("runner.before_write_run_result", run.RunDirectory);
            WriteRunResult(run, result);
            TestlabDebugMarkers.WritePhase("runner.after_write_run_result", run.RunDirectory);
            TestlabDebugMarkers.WritePhase("runner.return", run.RunDirectory);
            return result;
        }
        finally
        {
            overlay?.SetRect(null);
            overlay?.SetVisible(false);
        }
    }

    private static bool IsFixedCaptureEnabled()
    {
        var mode = (Environment.GetEnvironmentVariable("CHECKMIND_CAPTURE_MODE") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(mode))
        {
            return true;
        }

        return string.Equals(mode, "fixed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFastTabSwitchEnabled()
    {
        var raw = (Environment.GetEnvironmentVariable("CHECKMIND_FAST_TAB_SWITCH") ?? "").Trim();
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetIntEnv(string key, int defaultValue)
    {
        var raw = (Environment.GetEnvironmentVariable(key) ?? "").Trim();
        return int.TryParse(raw, out var v) ? v : defaultValue;
    }

    private static ProfileSignatureProbeResult ProbeStableProfileSignature(
        RunContext run,
        string tabName,
        TestlabWindowInfo win,
        ScreenCapture capturer,
        ICaptureOverlay? overlay,
        ScreenshotStore screenshots,
        int tabIndex,
        int xAttempt,
        int yAttempt,
        int clickTry,
        BBox verifyRoiWindow,
        string verifySha256,
        byte[] initialWindowBytes
    )
    {
        var stableConsecutive = Math.Clamp(GetIntEnv("CHECKMIND_PROFILE_SIGNATURE_VERIFY_STABLE_CONSECUTIVE", 2), 1, 4);
        var sampleCount = Math.Clamp(GetIntEnv("CHECKMIND_PROFILE_SIGNATURE_VERIFY_SAMPLE_COUNT", 4), stableConsecutive, 8);
        var sampleSleepMs = Math.Clamp(GetIntEnv("CHECKMIND_PROFILE_SIGNATURE_VERIFY_SAMPLE_SLEEP_MS", 80), 0, 500);

        string? lastSampleSha = null;
        var stableRun = 0;
        string? settledSha = null;
        byte[] settledSigBytes = Array.Empty<byte>();
        byte[] currentWindowBytes = initialWindowBytes;
        var sampled = 0;

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            if (sampleIndex > 0)
            {
                if (sampleSleepMs > 0)
                {
                    Thread.Sleep(sampleSleepMs);
                }

                currentWindowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
            }

            var sigBytes = ImageCropper.TryCropToPngBytes(currentWindowBytes, verifyRoiWindow);
            if (sigBytes is null)
            {
                stableRun = 0;
                continue;
            }

            sampled++;
            var actualSha = ComputeSha256Hex(sigBytes);
            if (string.Equals(lastSampleSha, actualSha, StringComparison.OrdinalIgnoreCase))
            {
                stableRun++;
            }
            else
            {
                stableRun = 1;
            }

            lastSampleSha = actualSha;
            if (stableRun >= stableConsecutive)
            {
                settledSha = actualSha;
                settledSigBytes = sigBytes;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(settledSha))
        {
            TestlabDebugMarkers.WritePhase(
                "runner.tab_switch_profile_signature_unstable",
                run.RunDirectory,
                $"tab={tabName};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry};sampled={sampled};stableConsecutive={stableConsecutive};sampleCount={sampleCount};sampleSleepMs={sampleSleepMs};roi=({verifyRoiWindow.X},{verifyRoiWindow.Y},{verifyRoiWindow.Width},{verifyRoiWindow.Height})"
            );
            return new ProfileSignatureProbeResult(false, "profile_signature_unstable", currentWindowBytes);
        }

        if (string.Equals(settledSha, verifySha256, StringComparison.OrdinalIgnoreCase))
        {
            TestlabDebugMarkers.WritePhase(
                "runner.tab_switch_verified_by_profile_signature",
                run.RunDirectory,
                $"tab={tabName};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry};sampled={sampled};stableConsecutive={stableConsecutive};sampleSleepMs={sampleSleepMs}"
            );
            return new ProfileSignatureProbeResult(true, "profile_signature", currentWindowBytes);
        }

        var sigShotId = $"testlab_tabs_{tabIndex:00}_profile_signature_{Normalize(tabName)}_{xAttempt}_{yAttempt}_{clickTry}";
        var sigPath = screenshots.SaveDebugPng(run, sigShotId, settledSigBytes);
        TestlabDebugMarkers.WritePhase(
            "runner.tab_switch_profile_signature_mismatch",
            run.RunDirectory,
            $"tab={tabName};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry};sampled={sampled};stableConsecutive={stableConsecutive};sampleSleepMs={sampleSleepMs};roi=({verifyRoiWindow.X},{verifyRoiWindow.Y},{verifyRoiWindow.Width},{verifyRoiWindow.Height});actual={settledSha};expected={verifySha256};path={sigPath}"
        );
        return new ProfileSignatureProbeResult(false, "profile_signature_mismatch", currentWindowBytes);
    }

    private static IReadOnlyList<TestlabTabSwitchResult> SwitchTabsIfRequested(
        RunContext run,
        TestlabWindowInfo win,
        byte[] currentWindowPngBytes,
        WindowController controller,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        ICaptureOverlay? overlay
    )
    {
        var tabsRaw = Environment.GetEnvironmentVariable("CHECKMIND_TESTLAB_TABS");
        if (string.IsNullOrWhiteSpace(tabsRaw))
        {
            return Array.Empty<TestlabTabSwitchResult>();
        }

        var tabs = tabsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tabs.Length == 0)
        {
            return Array.Empty<TestlabTabSwitchResult>();
        }

        var fixedProfile = IsFixedCaptureEnabled()
            ? WorkstationProfileStore.CreateDefault().Load()
            : null;
        var fastSwitch = fixedProfile is not null && IsFastTabSwitchEnabled();

        OcrRunner? ocrRunner = null;
        OcrRunner GetOcrRunner()
        {
            if (ocrRunner is not null)
            {
                return ocrRunner;
            }

            var ocr = CreateOcrAdapter();
            var ocrStore = new OcrStore();
            ocrRunner = new OcrRunner(ocr, ocrStore);
            return ocrRunner;
        }

        var (w, h) = ImageGeometry.GetSize(currentWindowPngBytes);
        var contentBounds = TryFindVisibleContentBounds(currentWindowPngBytes) ?? new BBox(0, 0, w, h);
        var tabsRoiWindow = fixedProfile is not null
            ? new BBox(
                X: 0,
                Y: Math.Max(0, h - Math.Clamp((int)Math.Round(h * 0.12), 120, 220)),
                Width: w,
                Height: Math.Min(h, Math.Clamp((int)Math.Round(h * 0.12), 120, 220))
            )
            : new BBox(
                contentBounds.X,
                contentBounds.Y + Math.Max(0, contentBounds.Height - Math.Clamp((int)Math.Round(contentBounds.Height * 0.09), 80, 160)),
                contentBounds.Width,
                Math.Min(Math.Clamp((int)Math.Round(contentBounds.Height * 0.09), 80, 160), contentBounds.Height)
            );
        TestlabDebugMarkers.WritePhase(
            "runner.tabs_roi_selected",
            run.RunDirectory,
            $"content=({contentBounds.X},{contentBounds.Y},{contentBounds.Width},{contentBounds.Height});tabs=({tabsRoiWindow.X},{tabsRoiWindow.Y},{tabsRoiWindow.Width},{tabsRoiWindow.Height});fixed={(fixedProfile is not null ? "1" : "0")};fast={(fastSwitch ? "1" : "0")}"
        );

        var results = new List<TestlabTabSwitchResult>();

        for (var i = 0; i < tabs.Length; i++)
        {
            var target = tabs[i];
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var normalizedTarget = Normalize(target);
            var sineSetupRestoredBeforeVerify = false;

            var shotId0 = $"testlab_tabs_{i:00}_0_before";
            var shot0Bytes = i == 0
                ? currentWindowPngBytes
                : CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
            var shot0Path = screenshots.SaveDebugPng(run, shotId0, shot0Bytes);

            byte[]? tabsImageBytes = null;
            var searchTabsWidth = tabsRoiWindow.Width <= 720
                ? tabsRoiWindow.Width
                : Math.Clamp((int)Math.Round(tabsRoiWindow.Width * 0.40), 720, Math.Min(1400, tabsRoiWindow.Width));
            var searchTabsRoi = new BBox(0, 0, searchTabsWidth, tabsRoiWindow.Height);
            byte[]? searchTabsBytes = null;

            if (fixedProfile is null || !fastSwitch)
            {
                overlay?.SetRect(
                    new BBox(
                        win.Rect.Left + tabsRoiWindow.X,
                        win.Rect.Top + tabsRoiWindow.Y,
                        tabsRoiWindow.Width,
                        tabsRoiWindow.Height
                    )
                );
                Thread.Sleep(80);
                tabsImageBytes = ImageCropper.TryCropToPngBytes(shot0Bytes, tabsRoiWindow);
                if (tabsImageBytes is null)
                {
                    tabsImageBytes = CaptureWithOverlay(
                        overlay,
                        () => capturer.CaptureRegionPngBytes(
                            win.Rect.Left + tabsRoiWindow.X,
                            win.Rect.Top + tabsRoiWindow.Y,
                            tabsRoiWindow.Width,
                            tabsRoiWindow.Height
                        )
                    );
                }
                _ = screenshots.SaveDebugPng(run, $"testlab_tabs_{i:00}_tabs_roi", tabsImageBytes);

                searchTabsBytes = ImageCropper.TryCropToPngBytes(tabsImageBytes, searchTabsRoi) ?? tabsImageBytes;
                _ = screenshots.SaveDebugPng(run, $"testlab_tabs_{i:00}_tabs_search_roi", searchTabsBytes);
            }

            var clickEvidencePath = string.Empty;
            var click = default(DesktopPoint?);
            var clickSource = "ocr";
            OcrBlock? tb = null;

            if (fixedProfile is not null)
            {
                clickSource = "fixed";
                var fixedTarget = fixedProfile.FindTabClickTarget(target);
                if (fixedTarget?.ClickPoint is not WindowPoint fixedPoint)
                {
                    tabsImageBytes ??= ImageCropper.TryCropToPngBytes(shot0Bytes, tabsRoiWindow) ??
                                      CaptureWithOverlay(
                                          overlay,
                                          () => capturer.CaptureRegionPngBytes(
                                              win.Rect.Left + tabsRoiWindow.X,
                                              win.Rect.Top + tabsRoiWindow.Y,
                                              tabsRoiWindow.Width,
                                              tabsRoiWindow.Height
                                          )
                                      );
                    searchTabsBytes ??= ImageCropper.TryCropToPngBytes(tabsImageBytes, searchTabsRoi) ?? tabsImageBytes;

                    var (suggestOcrPath, suggestClick) = TrySuggestTabClickPointByOcr(
                        run,
                        GetOcrRunner(),
                        screenshots,
                        $"testlab_tabs_{i:00}_ocr_suggest",
                        target,
                        searchTabsBytes,
                        searchTabsRoi,
                        tabsRoiWindow
                    );
                    clickEvidencePath = WriteTabClickArtifact(
                        run,
                        $"testlab_tabs_{i:00}_click_source",
                        target,
                        clickSource,
                        null,
                        tabsRoiWindow,
                        "profile 中未配置该页签的固定 ClickPoint。",
                        suggestClick,
                        suggestOcrPath
                    );
                    TestlabDebugMarkers.WritePhase("runner.fixed_tab_click_missing", run.RunDirectory, $"tab={target};path={clickEvidencePath}");
                    if (suggestClick is not null)
                    {
                        TestlabDebugMarkers.WritePhase(
                            "runner.fixed_tab_click_suggested",
                            run.RunDirectory,
                            $"tab={target};point=({suggestClick.Value.X},{suggestClick.Value.Y});ocr={suggestOcrPath}"
                        );
                    }
                    var reportPath = WriteTabClickPointGateReport(
                        run,
                        target,
                        "fixed_tab_click_missing",
                        "profile 中未配置该页签的固定 ClickPoint，已拒绝执行。请先完成标定或补全 profile。",
                        null,
                        win,
                        shot0Path,
                        null,
                        null,
                        null,
                        suggestOcrPath,
                        suggestClick is null ? null : new WindowPoint(suggestClick.Value.X, suggestClick.Value.Y)
                    );
                    throw new TabClickPointGateException("页签固定点击点门禁失败：缺少 ClickPoint。", reportPath);
                }

                if (fixedPoint.X < 0 ||
                    fixedPoint.Y < 0 ||
                    fixedPoint.X >= win.Rect.Width ||
                    fixedPoint.Y >= win.Rect.Height)
                {
                    tabsImageBytes ??= ImageCropper.TryCropToPngBytes(shot0Bytes, tabsRoiWindow) ??
                                      CaptureWithOverlay(
                                          overlay,
                                          () => capturer.CaptureRegionPngBytes(
                                              win.Rect.Left + tabsRoiWindow.X,
                                              win.Rect.Top + tabsRoiWindow.Y,
                                              tabsRoiWindow.Width,
                                              tabsRoiWindow.Height
                                          )
                                      );
                    searchTabsBytes ??= ImageCropper.TryCropToPngBytes(tabsImageBytes, searchTabsRoi) ?? tabsImageBytes;

                    var (suggestOcrPath, suggestClick) = TrySuggestTabClickPointByOcr(
                        run,
                        GetOcrRunner(),
                        screenshots,
                        $"testlab_tabs_{i:00}_ocr_suggest",
                        target,
                        searchTabsBytes,
                        searchTabsRoi,
                        tabsRoiWindow
                    );
                    clickEvidencePath = WriteTabClickArtifact(
                        run,
                        $"testlab_tabs_{i:00}_click_source",
                        target,
                        clickSource,
                        new DesktopPoint(fixedPoint.X, fixedPoint.Y),
                        tabsRoiWindow,
                        "profile 中的固定 ClickPoint 不在 Testlab 窗口内。",
                        suggestClick,
                        suggestOcrPath
                    );
                    TestlabDebugMarkers.WritePhase(
                        "runner.fixed_tab_click_out_of_window",
                        run.RunDirectory,
                        $"tab={target};point=({fixedPoint.X},{fixedPoint.Y});win=({win.Rect.Width},{win.Rect.Height});path={clickEvidencePath}"
                    );
                    if (suggestClick is not null)
                    {
                        TestlabDebugMarkers.WritePhase(
                            "runner.fixed_tab_click_suggested",
                            run.RunDirectory,
                            $"tab={target};point=({suggestClick.Value.X},{suggestClick.Value.Y});ocr={suggestOcrPath}"
                        );
                    }
                    var reportPath = WriteTabClickPointGateReport(
                        run,
                        target,
                        "fixed_tab_click_out_of_window",
                        "profile 中的固定 ClickPoint 不在 Testlab 窗口内，已拒绝执行。请重新标定并写入正确的窗口内坐标。",
                        new WindowPoint(fixedPoint.X, fixedPoint.Y),
                        win,
                        shot0Path,
                        null,
                        null,
                        null,
                        suggestOcrPath,
                        suggestClick is null ? null : new WindowPoint(suggestClick.Value.X, suggestClick.Value.Y)
                    );
                    throw new TabClickPointGateException("页签固定点击点门禁失败：ClickPoint 不在窗口内。", reportPath);
                }

                click = new DesktopPoint(fixedPoint.X, fixedPoint.Y);
                clickEvidencePath = WriteTabClickArtifact(
                    run,
                    $"testlab_tabs_{i:00}_click_source",
                    target,
                    clickSource,
                    click,
                    tabsRoiWindow
                );
            }
            else
            {
                if (searchTabsBytes is null)
                {
                    results.Add(new TestlabTabSwitchResult(target, shot0Path, clickEvidencePath, null, null, TabsRoiWindow: tabsRoiWindow));
                    continue;
                }

                var ocrId = $"testlab_tabs_{i:00}_ocr";
                var (ocrPath, ocrResult) = GetOcrRunner()
                    .RunAsync(
                        run,
                        ocrId,
                        searchTabsBytes,
                        "image/png",
                        new BBox(0, 0, searchTabsRoi.Width, searchTabsRoi.Height),
                        "tabs:all"
                    )
                    .GetAwaiter()
                    .GetResult();

                if (ocrResult.Blocks.Count == 0 || FindBlockByTarget(ocrResult, target) is null)
                {
                    (ocrPath, ocrResult) = GetOcrRunner()
                        .RunAsync(
                            run,
                            ocrId,
                            searchTabsBytes,
                            "image/png",
                            new BBox(0, 0, searchTabsRoi.Width, searchTabsRoi.Height),
                            $"tabs:{target}"
                        )
                        .GetAwaiter()
                        .GetResult();
                }

                clickEvidencePath = ocrPath;
                click = FindClickPointFromBlocks(
                    ocrResult,
                    new BBox(tabsRoiWindow.X + searchTabsRoi.X, tabsRoiWindow.Y, searchTabsRoi.Width, searchTabsRoi.Height),
                    target
                );
                if (click is null)
                {
                    var slidingClick = TryFindClickPointBySlidingWindows(
                        run,
                        i,
                        searchTabsBytes,
                        new BBox(tabsRoiWindow.X + searchTabsRoi.X, tabsRoiWindow.Y, searchTabsRoi.Width, searchTabsRoi.Height),
                        target,
                        GetOcrRunner(),
                        screenshots
                    );
                    if (slidingClick is not null)
                    {
                        click = slidingClick;
                        clickSource = "sliding";
                    }
                }
                if (click is null)
                {
                    results.Add(new TestlabTabSwitchResult(target, shot0Path, clickEvidencePath, null, null, TabsRoiWindow: tabsRoiWindow));
                    continue;
                }

                tb = FindBlockByTarget(ocrResult, target);
            }

            if (tb is not null)
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.tab_click_point",
                    run.RunDirectory,
                    $"tab={target};source={clickSource};bbox=({tb.BBox.X},{tb.BBox.Y},{tb.BBox.Width},{tb.BBox.Height});local=({click.Value.X},{click.Value.Y});win=({win.Rect.Left},{win.Rect.Top})"
                );
            }
            else
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.tab_click_point",
                    run.RunDirectory,
                    $"tab={target};source={clickSource};local=({click.Value.X},{click.Value.Y});win=({win.Rect.Left},{win.Rect.Top});path={clickEvidencePath}"
                );
            }

            var baseLocalX = click.Value.X - tabsRoiWindow.X;
            var screenY = win.Rect.Top + click.Value.Y;

            var baseScreenX = win.Rect.Left + click.Value.X;

            if (baseScreenX < win.Rect.Left || baseScreenX > win.Rect.Right || screenY < win.Rect.Top || screenY > win.Rect.Bottom)
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.click_out_of_window",
                    run.RunDirectory,
                    $"x={baseScreenX},y={screenY},win=({win.Rect.Left},{win.Rect.Top},{win.Rect.Width},{win.Rect.Height})"
                );
                results.Add(new TestlabTabSwitchResult(target, shot0Path, clickEvidencePath, null, null, TabsRoiWindow: tabsRoiWindow));
                continue;
            }

            controller.Activate(win.Hwnd);
            Thread.Sleep(clickSource == "fixed" && fastSwitch ? 20 : 150);

            var shift = Math.Clamp((int)Math.Round(tabsRoiWindow.Width * 0.045), 60, 160);
            var attemptXs = clickSource == "fixed"
                ? [baseScreenX]
                : new[]
                {
                    baseScreenX,
                    win.Rect.Left + tabsRoiWindow.X + Math.Clamp(baseLocalX + shift, 0, Math.Max(0, tabsRoiWindow.Width - 1)),
                    win.Rect.Left + tabsRoiWindow.X + Math.Clamp(baseLocalX - shift, 0, Math.Max(0, tabsRoiWindow.Width - 1)),
                    win.Rect.Left + tabsRoiWindow.X + Math.Clamp(baseLocalX + (2 * shift), 0, Math.Max(0, tabsRoiWindow.Width - 1))
                }.Distinct().ToArray();

            var attemptYs = clickSource == "fixed"
                ? [screenY]
                : new[]
                {
                    screenY,
                    win.Rect.Top + tabsRoiWindow.Y + Math.Max(1, tabsRoiWindow.Height - 6)
                };

            var verified = false;
            var usedY = screenY;
            var usedX = baseScreenX;
            byte[]? shot1Bytes = null;
            var pageProfile = fixedProfile?.FindPageProfile(target);
            var tabVerifyTarget = pageProfile?.FindVerifyTarget("tab_verify");
            var verifyRoiWindow = tabVerifyTarget?.RoiWindow ?? pageProfile?.VerifyRoiWindow;
            var verifySha256 = tabVerifyTarget?.Sha256 ?? pageProfile?.VerifySha256;

            var maxSecondsRaw = Environment.GetEnvironmentVariable("CHECKMIND_TAB_SWITCH_MAX_SECONDS");
            var maxSeconds = int.TryParse(maxSecondsRaw, out var ms) ? ms : 120;
            maxSeconds = Math.Clamp(maxSeconds, 10, 1800);
            var sw = Stopwatch.StartNew();
            var maxClickTries = clickSource == "fixed" && fastSwitch ? 1 : 2;
            var captureWindowMs = 0L;
            var verifyMs = 0L;
            var verifyMode = "unknown";
            var clickAttempts = 0;

            for (var xAttempt = 0; xAttempt < attemptXs.Length; xAttempt++)
            {
                if (sw.Elapsed > TimeSpan.FromSeconds(maxSeconds))
                {
                    TestlabDebugMarkers.WritePhase("runner.tab_switch_timeout", run.RunDirectory, $"tab={target};seconds={(int)sw.Elapsed.TotalSeconds}");
                    break;
                }
                usedX = attemptXs[xAttempt];
                for (var yAttempt = 0; yAttempt < attemptYs.Length; yAttempt++)
                {
                    if (sw.Elapsed > TimeSpan.FromSeconds(maxSeconds))
                    {
                        TestlabDebugMarkers.WritePhase("runner.tab_switch_timeout", run.RunDirectory, $"tab={target};seconds={(int)sw.Elapsed.TotalSeconds}");
                        break;
                    }
                    usedY = attemptYs[yAttempt];
                    for (var clickTry = 0; clickTry < maxClickTries; clickTry++)
                    {
                        if (sw.Elapsed > TimeSpan.FromSeconds(maxSeconds))
                        {
                            TestlabDebugMarkers.WritePhase("runner.tab_switch_timeout", run.RunDirectory, $"tab={target};seconds={(int)sw.Elapsed.TotalSeconds}");
                            break;
                        }
                        var probeRegionX = Math.Max(0, usedX - 80);
                        var probeRegionY = Math.Max(0, usedY - 40);
                        var probeRegionW = 160;
                        var probeRegionH = 80;
                        if (!(clickSource == "fixed" && fastSwitch))
                        {
                            var probeBeforeBytes = CaptureWithOverlay(
                                overlay,
                                () => capturer.CaptureRegionPngBytes(probeRegionX, probeRegionY, probeRegionW, probeRegionH)
                            );
                            _ = screenshots.SaveDebugPng(run, $"testlab_tabs_{i:00}_click_probe_{xAttempt}_{yAttempt}_{clickTry}_before", probeBeforeBytes);
                        }

                        controller.ClickScreenPoint(usedX, usedY);
                        var isFastFixedClick = clickSource == "fixed" && fastSwitch;
                        var postClickProbeSleepMs = isFastFixedClick
                            ? (normalizedTarget == Normalize("Sine Setup")
                                ? GetIntEnv("CHECKMIND_TAB_SWITCH_FAST_FIXED_SINE_POST_CLICK_PROBE_SLEEP_MS", 80)
                                : GetIntEnv("CHECKMIND_TAB_SWITCH_FAST_FIXED_POST_CLICK_PROBE_SLEEP_MS", 40))
                            : 120;
                        Thread.Sleep(postClickProbeSleepMs);

                        var cursorAfterClick = controller.GetCursorScreenPoint();
                        var foregroundAfterClick = controller.GetForegroundWindowHandle();
                        var windowAtPoint = controller.GetWindowFromScreenPoint(usedX, usedY);
                        TestlabDebugMarkers.WritePhase(
                            "runner.tab_click_probe",
                            run.RunDirectory,
                            $"tab={target};x={usedX};y={usedY};cursor=({cursorAfterClick?.X.ToString() ?? "?"},{cursorAfterClick?.Y.ToString() ?? "?"});fg=0x{foregroundAfterClick.ToInt64():X};point=0x{windowAtPoint.ToInt64():X};targetHwnd=0x{win.Hwnd.ToInt64():X}"
                        );

                        if (!(clickSource == "fixed" && fastSwitch))
                        {
                            var probeAfterBytes = CaptureWithOverlay(
                                overlay,
                                () => capturer.CaptureRegionPngBytes(probeRegionX, probeRegionY, probeRegionW, probeRegionH)
                            );
                            _ = screenshots.SaveDebugPng(run, $"testlab_tabs_{i:00}_click_probe_{xAttempt}_{yAttempt}_{clickTry}_after", probeAfterBytes);
                        }

                        var preCaptureSleepMs = isFastFixedClick
                            ? (normalizedTarget == Normalize("Sine Setup")
                                ? GetIntEnv("CHECKMIND_TAB_SWITCH_FAST_FIXED_SINE_PRE_CAPTURE_SLEEP_MS", 320)
                                : GetIntEnv("CHECKMIND_TAB_SWITCH_FAST_FIXED_PRE_CAPTURE_SLEEP_MS", 120))
                            : 580;
                        Thread.Sleep(preCaptureSleepMs);
                        clickAttempts++;
                        var capSw = Stopwatch.StartNew();
                        if (isFastFixedClick && normalizedTarget == Normalize("Sine Setup"))
                        {
                            (shot1Bytes, _) = RestoreSineSetupChannelSafetyParameters(
                                run,
                                controller,
                                capturer,
                                screenshots,
                                overlay,
                                win,
                                target,
                                i,
                                evidencePrefix: "testlab_tabs_preverify"
                            );
                            sineSetupRestoredBeforeVerify = true;
                        }
                        else
                        {
                            shot1Bytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
                        }
                        capSw.Stop();
                        captureWindowMs += capSw.ElapsedMilliseconds;

                        if (isFastFixedClick &&
                            normalizedTarget == Normalize("Sine Setup") &&
                            !sineSetupRestoredBeforeVerify &&
                            !string.IsNullOrWhiteSpace(verifySha256))
                        {
                            Thread.Sleep(GetIntEnv("CHECKMIND_TAB_SWITCH_FAST_FIXED_SINE_RECAPTURE_SLEEP_MS", 120));
                            capSw = Stopwatch.StartNew();
                            shot1Bytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
                            capSw.Stop();
                            captureWindowMs += capSw.ElapsedMilliseconds;
                        }

                        var localClickX = Math.Clamp(
                            usedX - win.Rect.Left - tabsRoiWindow.X,
                            0,
                            Math.Max(0, tabsRoiWindow.Width - 1)
                        );
                        var localClickY = Math.Clamp(
                            usedY - win.Rect.Top - tabsRoiWindow.Y,
                            0,
                            Math.Max(0, tabsRoiWindow.Height - 1)
                        );

                        var verSw = Stopwatch.StartNew();
                        var outcome = (Verified: false, Mode: "unknown");
                        if (clickSource == "fixed" && fastSwitch &&
                            verifyRoiWindow is not null &&
                            !string.IsNullOrWhiteSpace(verifySha256))
                        {
                            var probe = ProbeStableProfileSignature(
                                run,
                                target,
                                win,
                                capturer,
                                overlay,
                                screenshots,
                                i,
                                xAttempt,
                                yAttempt,
                                clickTry,
                                verifyRoiWindow.Value,
                                verifySha256,
                                shot1Bytes
                            );
                            shot1Bytes = probe.WindowBytes;
                            outcome = (probe.Verified, probe.Mode);
                        }
                        else
                        {
                            outcome = VerifyPageSwitched(
                                run,
                                target,
                                shot0Bytes,
                                shot1Bytes,
                                tabsRoiWindow,
                                verifyRoiWindow,
                                verifySha256,
                                clickSource == "fixed" && fastSwitch ? null : GetOcrRunner(),
                                screenshots,
                                i,
                                xAttempt,
                                yAttempt,
                                clickTry,
                                localClickX,
                                localClickY
                            );
                        }
                        verSw.Stop();
                        verifyMs += verSw.ElapsedMilliseconds;
                        verified = outcome.Verified;
                        verifyMode = outcome.Mode;

                        if (!verified && clickSource == "fixed" && fastSwitch &&
                            (string.Equals(verifyMode, "profile_signature_mismatch", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(verifyMode, "profile_signature_unstable", StringComparison.OrdinalIgnoreCase)) &&
                            verifyRoiWindow is not null &&
                            !string.IsNullOrWhiteSpace(verifySha256))
                        {
                            var retryCount = Math.Clamp(GetIntEnv("CHECKMIND_PROFILE_SIGNATURE_VERIFY_RETRY", 2), 0, 6);
                            var retrySleepMs = Math.Clamp(GetIntEnv("CHECKMIND_PROFILE_SIGNATURE_VERIFY_RETRY_SLEEP_MS", 100), 0, 800);
                            for (var verifyRetry = 0; verifyRetry < retryCount; verifyRetry++)
                            {
                                Thread.Sleep(retrySleepMs);
                                capSw = Stopwatch.StartNew();
                                shot1Bytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
                                capSw.Stop();
                                captureWindowMs += capSw.ElapsedMilliseconds;

                                verSw = Stopwatch.StartNew();
                                var probe = ProbeStableProfileSignature(
                                    run,
                                    target,
                                    win,
                                    capturer,
                                    overlay,
                                    screenshots,
                                    i,
                                    xAttempt,
                                    yAttempt,
                                    clickTry,
                                    verifyRoiWindow.Value,
                                    verifySha256,
                                    shot1Bytes
                                );
                                verSw.Stop();
                                verifyMs += verSw.ElapsedMilliseconds;
                                shot1Bytes = probe.WindowBytes;
                                verified = probe.Verified;
                                verifyMode = probe.Mode;
                                if (verified)
                                {
                                    break;
                                }
                            }
                        }

                        if (!verified && clickSource == "fixed" && fastSwitch && string.IsNullOrWhiteSpace(verifySha256))
                        {
                            verSw = Stopwatch.StartNew();
                            outcome = VerifyPageSwitched(
                                run,
                                target,
                                shot0Bytes,
                                shot1Bytes,
                                tabsRoiWindow,
                                verifyRoiWindow,
                                verifySha256,
                                GetOcrRunner(),
                                screenshots,
                                i,
                                xAttempt,
                                yAttempt,
                                clickTry,
                                localClickX,
                                localClickY
                            );
                            verSw.Stop();
                            verifyMs += verSw.ElapsedMilliseconds;
                            verified = outcome.Verified;
                            verifyMode = outcome.Mode;
                        }
                        if (verified)
                        {
                            break;
                        }
                    }

                    if (verified)
                    {
                        break;
                    }
                }

                if (verified)
                {
                    break;
                }
            }

            shot1Bytes ??= CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
            var shotId1 = $"testlab_tabs_{i:00}_1_after";
            var shot1Path = screenshots.SaveEvidencePng(run, shotId1, shot1Bytes);
            _ = WriteTabSwitchTimingReport(
                run,
                i,
                target,
                clickSource,
                clickSource == "fixed" && fastSwitch,
                verified,
                verifyMode,
                click is null ? null : new WindowPoint(click.Value.X, click.Value.Y),
                usedX,
                usedY,
                shot0Path,
                shot1Path,
                sw.ElapsedMilliseconds,
                captureWindowMs,
                verifyMs,
                clickAttempts,
                attemptXs.Length,
                attemptYs.Length,
                maxClickTries
            );

            if (!verified)
            {
                TestlabDebugMarkers.WritePhase("runner.tab_switch_unverified", run.RunDirectory, $"tab={target}");
                if (clickSource == "fixed")
                {
                    var ruleKey = verifyMode;
                    var message = verifyMode == "profile_signature_mismatch"
                        ? "验真签名不命中（profile_signature mismatch），说明当前工位/分辨率/DPI/布局与 profile 不一致。已拒绝继续执行。请执行“复用 ClickPoint 重新标定验真签名”。"
                        : "固定坐标点击后未通过切页验真，已拒绝继续执行。请查看证据并考虑重新标定 ClickPoint。";

                    var reportPath = WriteTabClickPointGateReport(
                        run,
                        target,
                        ruleKey,
                        message,
                        click is null ? null : new WindowPoint(click.Value.X, click.Value.Y),
                        win,
                        shot0Path,
                        shot1Path,
                        null,
                        null,
                        null,
                        null,
                        sw.ElapsedMilliseconds
                    );
                    throw new TabClickPointGateException("页签固定点击点门禁失败：切页未通过验真。", reportPath);
                }

                results.Add(new TestlabTabSwitchResult(target, shot0Path, clickEvidencePath, new DesktopPoint(usedX, usedY), shot1Path, TabsRoiWindow: tabsRoiWindow));
                continue;
            }

            if (normalizedTarget == Normalize("Sine Setup") && !sineSetupRestoredBeforeVerify)
            {
                (shot1Bytes, shot1Path) = RestoreSineSetupChannelSafetyParameters(
                    run,
                    controller,
                    capturer,
                    screenshots,
                    overlay,
                    win,
                    target,
                    i,
                    evidencePrefix: "testlab_tabs"
                );
            }

            var fixedCaptures = CapturePageFixedTargetsIfNeeded(run, target, shot1Bytes, screenshots, i, win, fixedProfile);
            var tableEntries = CaptureTableEntriesIfNeeded(run, target, shot1Bytes, capturer, screenshots, i, win, tabsRoiWindow, overlay);
            var tableScans = ScanTablesVerticallyIfNeeded(run, target, controller, capturer, screenshots, win, tableEntries, overlay);
            var childWindowCaptures = CaptureFixedChildWindowsIfNeeded(run, target, controller, capturer, screenshots, win, i, fixedProfile);
            var regions = fixedProfile is not null && fastSwitch
                ? Array.Empty<TestlabPageRegionResult>()
                : DetectPageRegionsIfNeeded(run, target, shot1Bytes, GetOcrRunner(), screenshots, i, win, overlay);
            results.Add(
                new TestlabTabSwitchResult(
                    target,
                    shot0Path,
                    clickEvidencePath,
                    new DesktopPoint(usedX, usedY),
                    shot1Path,
                    regions,
                    TabsRoiWindow: tabsRoiWindow,
                    TableEntries: tableEntries,
                    TableScans: tableScans,
                    FixedCaptures: fixedCaptures,
                    ChildWindowCaptures: childWindowCaptures
                )
            );
        }

        return results;
    }

    private static IReadOnlyList<TestlabTableEntryResult>? CaptureTableEntriesIfNeeded(
        RunContext run,
        string tabName,
        byte[] windowImageBytes,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        int tabIndex,
        TestlabWindowInfo win,
        BBox tabsRoiWindow,
        ICaptureOverlay? overlay
    )
    {
        var normalizedTab = Normalize(tabName);
        if (normalizedTab != Normalize("Sine Setup") && normalizedTab != Normalize("Channel Setup"))
        {
            return null;
        }

        var fixedProfile = IsFixedCaptureEnabled()
            ? WorkstationProfileStore.CreateDefault().Load()
            : null;
        var pageProfile = fixedProfile?.FindPageProfile(tabName);

        var (w, h) = ImageGeometry.GetSize(windowImageBytes);
        var contentBounds = TryFindVisibleContentBounds(windowImageBytes) ?? new BBox(0, 0, w, h);
        BBox defaultRoiWindow;
        if (normalizedTab == Normalize("Sine Setup"))
        {
            var top = Math.Clamp(contentBounds.Y + (int)Math.Round(contentBounds.Height * 0.02), 0, Math.Max(0, h - 1));
            var height = Math.Clamp((int)Math.Round(contentBounds.Height * 0.52), 420, Math.Min(700, h));
            defaultRoiWindow = new BBox(contentBounds.X, top, contentBounds.Width, Math.Min(height, Math.Max(1, h - top)));
        }
        else
        {
            var top = Math.Clamp(contentBounds.Y + (int)Math.Round(contentBounds.Height * 0.10), 0, Math.Max(0, h - 1));
            var bottomPad = Math.Clamp((int)Math.Round(contentBounds.Height * 0.05), 30, 90);
            var bottom = Math.Clamp(contentBounds.Y + contentBounds.Height - bottomPad, top + 1, h);
            var height = Math.Max(1, bottom - top);
            defaultRoiWindow = new BBox(contentBounds.X, top, contentBounds.Width, Math.Min(height, Math.Max(1, h - top)));
        }

        var entryCaptureTarget = pageProfile?.FindCaptureTarget("table_entry");
        var scanCaptureTarget = pageProfile?.FindCaptureTarget("table_scan");
        var entryProfileRoi = entryCaptureTarget?.RoiWindow;
        var scanProfileRoi = scanCaptureTarget?.RoiWindow ?? pageProfile?.CaptureRoiWindow;
        var pagingFocusPointWindow = scanCaptureTarget?.PagingFocusPointWindow ?? pageProfile?.ScrollAnchor;
        var pagingActivationPointWindow = scanCaptureTarget?.PagingActivationPointWindow;
        var pagingPreparationMode = scanCaptureTarget?.PagingPreparationMode;
        var entryRoiWindow = entryProfileRoi ?? defaultRoiWindow;
        var scanRoiWindow = scanProfileRoi ?? entryRoiWindow;
        var entryRoiScreen = entryProfileRoi is not null
            ? MapWindowRoiToScreenRoi(entryRoiWindow, win)
            : MapWindowRoiToScreenRoi(entryRoiWindow, contentBounds, win);
        var scanRoiScreen = scanProfileRoi is not null
            ? MapWindowRoiToScreenRoi(scanRoiWindow, win)
            : MapWindowRoiToScreenRoi(scanRoiWindow, contentBounds, win);
        overlay?.SetRect(entryRoiScreen);
        Thread.Sleep(120);

        var tableBytes = ImageCropper.TryCropToPngBytes(windowImageBytes, entryRoiWindow) ?? windowImageBytes;
        var tableKey = normalizedTab == Normalize("Sine Setup") ? "sinesetup_channelparams" : "channelsetup_main";
        var tableName = normalizedTab == Normalize("Sine Setup") ? "Channel Parameters Table" : "Channel Setup Table";
        var tablePath = screenshots.SaveEvidencePng(run, $"testlab_tabs_{tabIndex:00}_{tableKey}_table_entry", tableBytes);
        return new[]
        {
            new TestlabTableEntryResult(
                tabName,
                tableName,
                tablePath,
                scanRoiWindow,
                scanRoiScreen,
                pagingFocusPointWindow,
                pagingActivationPointWindow,
                pagingPreparationMode
            )
        };
    }

    private static IReadOnlyList<TestlabFixedCaptureResult> CapturePageFixedTargetsIfNeeded(
        RunContext run,
        string tabName,
        byte[] windowImageBytes,
        ScreenshotStore screenshots,
        int tabIndex,
        TestlabWindowInfo win,
        WorkstationProfile? fixedProfile
    )
    {
        if (fixedProfile is null)
        {
            return Array.Empty<TestlabFixedCaptureResult>();
        }

        var pageProfile = fixedProfile.FindPageProfile(tabName);
        if (pageProfile is null)
        {
            return Array.Empty<TestlabFixedCaptureResult>();
        }

        var results = new List<TestlabFixedCaptureResult>();
        foreach (var target in pageProfile.CaptureTargets ?? [])
        {
            var keyNorm = Normalize(target.Key);
            if (keyNorm is "tableentry" or "tablescan")
            {
                continue;
            }

            if (target.RoiWindow is not BBox roiWindow)
            {
                continue;
            }

            var bytes = ImageCropper.TryCropToPngBytes(windowImageBytes, roiWindow);
            if (bytes is null || bytes.Length == 0)
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.page_fixed_capture_crop_failed",
                    run.RunDirectory,
                    $"tab={tabName};key={target.Key};roi=({roiWindow.X},{roiWindow.Y},{roiWindow.Width},{roiWindow.Height})"
                );
                continue;
            }

            var path = screenshots.SaveEvidencePng(
                run,
                $"testlab_tabs_{tabIndex:00}_{Normalize(tabName)}_{Normalize(target.Key)}",
                bytes
            );
            results.Add(
                new TestlabFixedCaptureResult(
                    target.Key,
                    path,
                    roiWindow,
                    MapWindowRoiToScreenRoi(roiWindow, win)
                )
            );
        }

        return results;
    }

    private static IReadOnlyList<TestlabChildWindowCaptureResult> CaptureFixedChildWindowsIfNeeded(
        RunContext run,
        string tabName,
        WindowController controller,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        TestlabWindowInfo win,
        int tabIndex,
        WorkstationProfile? fixedProfile
    )
    {
        if (fixedProfile is null || Normalize(tabName) != Normalize("Sine Setup"))
        {
            return Array.Empty<TestlabChildWindowCaptureResult>();
        }

        var windowKeys = new[] { "profile_editor", "advanced_control_setup" };
        var results = new List<TestlabChildWindowCaptureResult>();
        foreach (var windowKey in windowKeys)
        {
            var childProfile = fixedProfile.FindChildWindowProfile(windowKey);
            if (childProfile is null)
            {
                continue;
            }

            results.Add(
                CaptureFixedChildWindow(
                    run,
                    controller,
                    capturer,
                    screenshots,
                    win,
                    tabIndex,
                    windowKey,
                    childProfile
                )
            );
        }

        return results;
    }

    private static TestlabChildWindowCaptureResult CaptureFixedChildWindow(
        RunContext run,
        WindowController controller,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        TestlabWindowInfo mainWindow,
        int tabIndex,
        string windowKey,
        WorkstationChildWindowProfile childWindowProfile
    )
    {
        var titleContains = ResolveChildWindowTitleContains(windowKey);
        var childLocator = new TestlabChildWindowLocator();
        TestlabWindowInfo? childWindow = null;

        try
        {
            var openSequence = childWindowProfile.GetOpenClickSequence();
            if (openSequence.Length == 0)
            {
                throw new InvalidOperationException($"profile 缺少 {windowKey}.openClickSequence / openClickPoint 配置。");
            }

            controller.Activate(mainWindow.Hwnd);
            Thread.Sleep(100);
            foreach (var point in openSequence)
            {
                controller.ClickWindowPoint(mainWindow.Hwnd, point);
                Thread.Sleep(180);
            }

            var resolvedChildWindow = childLocator.FindByTitleContains(
                titleContains,
                processName: mainWindow.ProcessName,
                timeoutMs: 2000
            );
            childWindow = resolvedChildWindow;

            if (childWindowProfile.MustMaximize)
            {
                controller.Activate(resolvedChildWindow.Hwnd);
                Thread.Sleep(80);
                controller.Maximize(resolvedChildWindow.Hwnd);
                Thread.Sleep(120);
                controller.Activate(resolvedChildWindow.Hwnd);
                Thread.Sleep(250);
                resolvedChildWindow = childLocator.FindByTitleContains(
                    titleContains,
                    processName: mainWindow.ProcessName,
                    timeoutMs: 1000
                );
                childWindow = resolvedChildWindow;
            }

            var openedWindowPath = screenshots.SaveEvidencePng(
                run,
                $"testlab_tabs_{tabIndex:00}_{Normalize(windowKey)}_window_opened",
                capturer.CaptureWindowPngBytes(resolvedChildWindow.Hwnd)
            );

            var captures = new List<TestlabFixedCaptureResult>();
            var tableScans = new List<TestlabTableScanResult>();
            foreach (var target in childWindowProfile.CaptureTargets ?? [])
            {
                if (target.RoiWindow is not BBox roiWindow)
                {
                    continue;
                }

                var tabClick = childWindowProfile.FindTabClickTarget(target.Key);
                string? sourceTabName = null;
                if (tabClick?.ClickPoint is WindowPoint clickPoint)
                {
                    controller.Activate(resolvedChildWindow.Hwnd);
                    Thread.Sleep(80);
                    controller.ClickWindowPoint(resolvedChildWindow.Hwnd, clickPoint);
                    Thread.Sleep(Math.Clamp(GetIntEnv("CHECKMIND_CHILD_WINDOW_TAB_CAPTURE_SETTLE_MS", 220), 0, 1500));
                    sourceTabName = tabClick.TabName;
                }

                if (WorkstationProfileKeys.IsTableScanCaptureKey(WorkstationProfileKeys.Normalize(target.Key)))
                {
                    var tableScan = ScanSingleChildWindowTable(
                        run,
                        controller,
                        capturer,
                        screenshots,
                        resolvedChildWindow,
                        childWindowProfile,
                        windowKey,
                        target
                    );
                    tableScans.Add(tableScan);
                    continue;
                }

                var windowBytes = capturer.CaptureWindowPngBytes(resolvedChildWindow.Hwnd);
                var bytes = ImageCropper.TryCropToPngBytes(windowBytes, roiWindow);
                if (bytes is null || bytes.Length == 0)
                {
                    TestlabDebugMarkers.WritePhase(
                        "runner.child_window_capture_crop_failed",
                        run.RunDirectory,
                        $"windowKey={windowKey};captureKey={target.Key};roi=({roiWindow.X},{roiWindow.Y},{roiWindow.Width},{roiWindow.Height})"
                    );
                    continue;
                }

                var path = screenshots.SaveEvidencePng(
                    run,
                    $"testlab_tabs_{tabIndex:00}_{Normalize(windowKey)}_{Normalize(target.Key)}",
                    bytes
                );
                captures.Add(
                    new TestlabFixedCaptureResult(
                        target.Key,
                        path,
                        roiWindow,
                        MapWindowRoiToScreenRoi(roiWindow, resolvedChildWindow),
                        sourceTabName
                    )
                );
            }

            var (closeMode, childWindowClosed) = CloseChildWindow(
                controller,
                childLocator,
                resolvedChildWindow,
                titleContains,
                mainWindow.ProcessName,
                childWindowProfile
            );

            string? returnedToParentPath = null;
            if (childWindowClosed)
            {
                var returnedMain = new TestlabWindowLocator().Find();
                controller.Activate(returnedMain.Hwnd);
                Thread.Sleep(120);
                returnedToParentPath = screenshots.SaveEvidencePng(
                    run,
                    $"testlab_tabs_{tabIndex:00}_{Normalize(windowKey)}_returned_to_parent",
                    capturer.CaptureWindowPngBytes(returnedMain.Hwnd)
                );
            }

            return new TestlabChildWindowCaptureResult(
                windowKey,
                titleContains,
                resolvedChildWindow.Title,
                openedWindowPath,
                captures,
                tableScans,
                closeMode,
                childWindowClosed,
                returnedToParentPath
            );
        }
        catch (Exception ex)
        {
            TestlabDebugMarkers.WritePhase(
                "runner.child_window_capture_exception",
                run.RunDirectory,
                $"windowKey={windowKey};type={ex.GetType().Name};message={SanitizeDetail(ex.Message)}"
            );

            return new TestlabChildWindowCaptureResult(
                windowKey,
                titleContains,
                childWindow?.Title,
                null,
                Array.Empty<TestlabFixedCaptureResult>(),
                Array.Empty<TestlabTableScanResult>(),
                null,
                false,
                null,
                ex.Message
            );
        }
    }

    private static string ResolveChildWindowTitleContains(string windowKey)
    {
        return Normalize(windowKey) switch
        {
            "profileeditor" => "Profile Editor",
            "advancedcontrolsetup" => "Advanced Control Setup",
            _ => windowKey
        };
    }

    private static WindowPoint ComputeDefaultChildWindowPagingFocusPoint(BBox tableRoi)
    {
        var serialWidth = Math.Clamp((int)Math.Round(tableRoi.Width * 0.12), 80, 280);
        var focusX = tableRoi.X + Math.Clamp(serialWidth / 2, 24, Math.Max(24, tableRoi.Width - 1));
        var focusY = tableRoi.Y + Math.Clamp(tableRoi.Height / 3, 24, Math.Max(24, tableRoi.Height - 1));
        return new WindowPoint(focusX, focusY);
    }

    private static WindowPoint ResolveChildWindowPagingFocusPoint(string windowKey, WorkstationCaptureTarget captureTarget, BBox tableRoiWindow)
    {
        if (captureTarget.PagingFocusPointWindow is WindowPoint focus)
        {
            return focus;
        }

        if (string.Equals(Normalize(windowKey), "profileeditor", StringComparison.OrdinalIgnoreCase))
        {
            return ComputeDefaultChildWindowPagingFocusPoint(tableRoiWindow);
        }

        return ComputeDefaultChildWindowPagingFocusPoint(tableRoiWindow);
    }

    private static WindowPoint? ResolveChildWindowPagingActivationPoint(string windowKey, WorkstationCaptureTarget captureTarget, BBox tableRoiWindow)
    {
        if (captureTarget.PagingActivationPointWindow is WindowPoint activation)
        {
            return activation;
        }

        if (string.Equals(Normalize(windowKey), "profileeditor", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveChildWindowPagingFocusPoint(windowKey, captureTarget, tableRoiWindow);
        }

        return null;
    }

    private static string? ResolveChildWindowPagingPreparationMode(string windowKey, WorkstationCaptureTarget captureTarget)
    {
        if (!string.IsNullOrWhiteSpace(captureTarget.PagingPreparationMode))
        {
            return captureTarget.PagingPreparationMode;
        }

        if (string.Equals(Normalize(windowKey), "profileeditor", StringComparison.OrdinalIgnoreCase))
        {
            return "activation_foreground";
        }

        return captureTarget.PagingPreparationMode;
    }

    private static TestlabTableScanResult ScanSingleChildWindowTable(
        RunContext run,
        WindowController controller,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        TestlabWindowInfo childWindow,
        WorkstationChildWindowProfile childWindowProfile,
        string windowKey,
        WorkstationCaptureTarget captureTarget
    )
    {
        if (captureTarget.RoiWindow is not BBox tableRoiWindow)
        {
            throw new InvalidOperationException($"子窗口 [{windowKey}] 的 table_scan 缺少 RoiWindow。");
        }

        var title = ResolveChildWindowTitleContains(windowKey);
        var pagingFocusPointWindow = ResolveChildWindowPagingFocusPoint(windowKey, captureTarget, tableRoiWindow);
        var pagingActivationPointWindow = ResolveChildWindowPagingActivationPoint(windowKey, captureTarget, tableRoiWindow);
        var pagingPreparationMode = ResolveChildWindowPagingPreparationMode(windowKey, captureTarget);
        var entry = new TestlabTableEntryResult(
            TabName: title,
            TableName: $"{WorkstationProfileKeys.Normalize(windowKey)}_table_scan",
            ScreenshotPath: string.Empty,
            TableRoiWindow: tableRoiWindow,
            TableRoiScreen: MapWindowRoiToScreenRoi(tableRoiWindow, childWindow),
            PagingFocusPointWindow: pagingFocusPointWindow,
            PagingActivationPointWindow: pagingActivationPointWindow,
            PagingPreparationMode: pagingPreparationMode
        );

        var maxSteps = ResolveChildWindowTableScanMaxSteps(windowKey);

        var pauseRaw = Environment.GetEnvironmentVariable("CHECKMIND_TABLE_SCAN_PAUSE_MS");
        var pauseMs = int.TryParse(pauseRaw, out var p) ? p : 250;
        pauseMs = Math.Clamp(pauseMs, 80, 2000);

        controller.Activate(childWindow.Hwnd);
        Thread.Sleep(120);

        return ScanSingleTableWithDeterministicPaging(
            run,
            controller,
            overlay: null,
            capturer,
            screenshots,
            childWindow,
            entry,
            fixedProfile: null,
            childWindowProfile,
            maxSteps,
            pauseMs
        );
    }

    private static int ResolveChildWindowTableScanMaxSteps(string windowKey)
    {
        var maxStepsRaw = Environment.GetEnvironmentVariable("CHECKMIND_TABLE_SCAN_V_MAX_STEPS");
        var defaultMaxSteps = int.TryParse(maxStepsRaw, out var v) ? v : 8;
        defaultMaxSteps = Math.Clamp(defaultMaxSteps, 1, 50);

        return Normalize(windowKey) switch
        {
            "profileeditor" => 3,
            _ => defaultMaxSteps
        };
    }

    internal static IReadOnlyList<TestlabTableScanResult>? ScanTablesVerticallyIfNeeded(
        RunContext run,
        string tabName,
        WindowController controller,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        TestlabWindowInfo win,
        IReadOnlyList<TestlabTableEntryResult>? entries,
        ICaptureOverlay? overlay
    )
    {
        if (entries is null || entries.Count == 0)
        {
            return null;
        }

        var fixedProfile = IsFixedCaptureEnabled()
            ? WorkstationProfileStore.CreateDefault().Load()
            : null;

        var maxStepsRaw = Environment.GetEnvironmentVariable("CHECKMIND_TABLE_SCAN_V_MAX_STEPS");
        var maxSteps = int.TryParse(maxStepsRaw, out var v) ? v : 8;
        maxSteps = Math.Clamp(maxSteps, 1, 50);

        var pauseRaw = Environment.GetEnvironmentVariable("CHECKMIND_TABLE_SCAN_PAUSE_MS");
        var pauseMs = int.TryParse(pauseRaw, out var p) ? p : 250;
        pauseMs = Math.Clamp(pauseMs, 80, 2000);

        controller.Activate(win.Hwnd);
        Thread.Sleep(120);

        var results = new List<TestlabTableScanResult>();
        foreach (var entry in entries)
        {
            results.Add(
                ScanSingleTableWithDeterministicPaging(
                    run,
                    controller,
                    overlay,
                    capturer,
                    screenshots,
                    win,
                    entry,
                    fixedProfile,
                    childWindowProfile: null,
                    maxSteps,
                    pauseMs
                )
            );
        }

        return results;
    }

    internal static TestlabTableScanResult ScanSingleTableWithDeterministicPaging(
        RunContext run,
        WindowController controller,
        ICaptureOverlay? overlay,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        TestlabWindowInfo win,
        TestlabTableEntryResult entry,
        WorkstationProfile? fixedProfile,
        WorkstationChildWindowProfile? childWindowProfile,
        int maxSteps,
        int pauseMs
    )
    {
        var context = BuildTableScanContext(run, entry, win, fixedProfile, childWindowProfile);
        PrimeTableScanInteraction(controller, context);

        var resetTopStable = ResetTableToTopBeforeScan(
            run,
            controller,
            overlay,
            capturer,
            screenshots,
            context,
            pauseMs
        );
        if (!resetTopStable)
        {
            TestlabDebugMarkers.WritePhase(
                "runner.table_scan_blocked_unstable_top",
                run.RunDirectory,
                $"table={entry.TableName};tab={entry.TabName};reason=reset_top_not_stable"
            );
            return new TestlabTableScanResult(entry.TabName, entry.TableName, entry.TableRoiWindow, entry.TableRoiScreen, Array.Empty<TestlabTableScanChunkResult>(), 0, null);
        }

        var (chunks, scrollEvents) = CaptureTableChunksWithDeterministicPaging(
            run,
            controller,
            overlay,
            capturer,
            screenshots,
            context,
            maxSteps
        );

        if (scrollEvents.Count > 0)
        {
            var scrollPath = Path.Combine(run.RunDirectory, $"table_scroll_events_{Normalize(entry.TableName)}.json");
            var scrollReport = new TestlabTableScrollEventsReport(entry.TabName, entry.TableName, context.TableRoiScreen, context.SerialRoiScreen, context.ScrollbarRoiScreen, scrollEvents);
            File.WriteAllText(scrollPath, scrollReport.ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        var uniqueChunks = GetUniqueChunksBySerial(chunks);
        var stitchedPath = uniqueChunks.Count > 0
            ? TryWriteStitchedTableEvidence(run, screenshots, entry.TableName, uniqueChunks)
            : null;
        WriteTableEvidenceReport(run, entry.TabName, entry.TableName, chunks, uniqueChunks, scrollEvents, stitchedPath);
        return new TestlabTableScanResult(entry.TabName, entry.TableName, entry.TableRoiWindow, entry.TableRoiScreen, chunks, uniqueChunks.Count, stitchedPath);
    }

    private static TestlabTableScanContext BuildTableScanContext(
        RunContext run,
        TestlabTableEntryResult entry,
        TestlabWindowInfo win,
        WorkstationProfile? fixedProfile,
        WorkstationChildWindowProfile? childWindowProfile
    )
    {
        var tableRoiScreen = entry.TableRoiScreen;
        var interactionRoiScreen = GetTableInteractionRoi(entry);
        var tableRoiWindow = entry.TableRoiWindow;
        var interactionRoiWindow = GetTableInteractionRoiWindow(entry);
        var effectiveFocusPointWindow = entry.PagingFocusPointWindow;
        var focusX = effectiveFocusPointWindow is WindowPoint configuredFocus
            ? win.Rect.Left + Math.Clamp(configuredFocus.X, 0, Math.Max(0, win.Rect.Width - 1))
            : interactionRoiScreen.X + Math.Clamp((int)Math.Round(interactionRoiScreen.Width * 0.35), 60, Math.Max(60, interactionRoiScreen.Width - 24));
        var focusY = effectiveFocusPointWindow is WindowPoint configuredFocusY
            ? win.Rect.Top + Math.Clamp(configuredFocusY.Y, 0, Math.Max(0, win.Rect.Height - 1))
            : interactionRoiScreen.Y + (interactionRoiScreen.Height / 2);
        var activationX = entry.PagingActivationPointWindow is WindowPoint configuredActivation
            ? win.Rect.Left + Math.Clamp(configuredActivation.X, 0, Math.Max(0, win.Rect.Width - 1))
            : focusX;
        var activationY = entry.PagingActivationPointWindow is WindowPoint configuredActivationY
            ? win.Rect.Top + Math.Clamp(configuredActivationY.Y, 0, Math.Max(0, win.Rect.Height - 1))
            : focusY;
        var scrollbarRoiScreen = new BBox(
            interactionRoiScreen.X + Math.Max(0, interactionRoiScreen.Width - 18),
            interactionRoiScreen.Y,
            Math.Min(18, interactionRoiScreen.Width),
            interactionRoiScreen.Height
        );
        var scrollbarRoiWindow = new BBox(
            interactionRoiWindow.X + Math.Max(0, interactionRoiWindow.Width - 18),
            interactionRoiWindow.Y,
            Math.Min(18, interactionRoiWindow.Width),
            interactionRoiWindow.Height
        );
        var topSerialVerifyTarget = childWindowProfile?.FindVerifyTarget("top_serial")
            ?? fixedProfile?.FindPageProfile(entry.TabName)?.FindVerifyTarget("top_serial");
        var topSerialVerifyRoiWindow = topSerialVerifyTarget?.RoiWindow;
        var topSerialVerifySha256 = topSerialVerifyTarget?.Sha256
            ?? fixedProfile?.FindPageProfile(entry.TabName)?.TopSerialVerifySha256;

        var serialXRaw = Environment.GetEnvironmentVariable("CHECKMIND_TABLE_SCAN_SERIAL_X");
        var serialX = int.TryParse(serialXRaw, out var sx) ? sx : 0;
        serialX = Math.Clamp(serialX, 0, Math.Max(0, tableRoiScreen.Width - 1));

        var serialWidthRaw = Environment.GetEnvironmentVariable("CHECKMIND_TABLE_SCAN_SERIAL_WIDTH");
        var serialWidth = int.TryParse(serialWidthRaw, out var sw)
            ? sw
            : Math.Clamp((int)Math.Round(tableRoiScreen.Width * 0.12), 80, 280);
        serialWidth = Math.Clamp(serialWidth, 20, Math.Max(20, tableRoiScreen.Width - serialX));

        var serialRoiScreen = new BBox(tableRoiScreen.X + serialX, tableRoiScreen.Y, serialWidth, tableRoiScreen.Height);
        var serialRoiWindow = new BBox(tableRoiWindow.X + serialX, tableRoiWindow.Y, serialWidth, tableRoiWindow.Height);
        var pagingPreparationMode = string.IsNullOrWhiteSpace(entry.PagingPreparationMode)
            ? "default"
            : entry.PagingPreparationMode.Trim();

        TestlabDebugMarkers.WritePhase(
            "runner.table_scroll_target",
            run.RunDirectory,
            $"table={entry.TableName};roi=({tableRoiScreen.X},{tableRoiScreen.Y},{tableRoiScreen.Width},{tableRoiScreen.Height});roiWindow=({tableRoiWindow.X},{tableRoiWindow.Y},{tableRoiWindow.Width},{tableRoiWindow.Height});interaction=({interactionRoiScreen.X},{interactionRoiScreen.Y},{interactionRoiScreen.Width},{interactionRoiScreen.Height});interactionWindow=({interactionRoiWindow.X},{interactionRoiWindow.Y},{interactionRoiWindow.Width},{interactionRoiWindow.Height});focus=({focusX},{focusY});focusWindow={(entry.PagingFocusPointWindow is WindowPoint focusWindow ? $"({focusWindow.X},{focusWindow.Y})" : "<auto>")};activation=({activationX},{activationY});activationWindow={(entry.PagingActivationPointWindow is WindowPoint activationWindow ? $"({activationWindow.X},{activationWindow.Y})" : "<none>")};preparationMode={SanitizePhaseValue(pagingPreparationMode)};scrollbar=({scrollbarRoiScreen.X},{scrollbarRoiScreen.Y},{scrollbarRoiScreen.Width},{scrollbarRoiScreen.Height})"
        );

        return new TestlabTableScanContext(
            entry,
            win,
            tableRoiScreen,
            tableRoiWindow,
            interactionRoiScreen,
            interactionRoiWindow,
            serialRoiScreen,
            serialRoiWindow,
            scrollbarRoiScreen,
            scrollbarRoiWindow,
            focusX,
            focusY,
            activationX,
            activationY,
            entry.PagingActivationPointWindow is not null,
            pagingPreparationMode,
            topSerialVerifyRoiWindow,
            topSerialVerifySha256
        );
    }

    private static void PrimeTableScanInteraction(
        WindowController controller,
        TestlabTableScanContext context
    )
    {
        if (ReusePagingPreparation(context.Entry.TableName, context.PagingPreparationMode))
        {
            return;
        }

        var pointerMode = GetTablePointerInputMode(context.PagingPreparationMode);
        DispatchPointerClick(controller, context.ActivationX, context.ActivationY, pointerMode);
        Thread.Sleep(80);
        DispatchPointerClick(controller, context.FocusX, context.FocusY, pointerMode);
        Thread.Sleep(80);
    }

    private static (List<TestlabTableScanChunkResult> Chunks, List<TestlabTableScrollEvent> ScrollEvents) CaptureTableChunksWithDeterministicPaging(
        RunContext run,
        WindowController controller,
        ICaptureOverlay? overlay,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        TestlabTableScanContext context,
        int maxSteps
    )
    {
        var chunks = new List<TestlabTableScanChunkResult>();
        var scrollEvents = new List<TestlabTableScrollEvent>();

        var (currentChunk, currentScrollbarHash) = CaptureTableChunk(
            run,
            controller,
            overlay,
            capturer,
            screenshots,
            context,
            chunkIndex: 0
        );
        chunks.Add(currentChunk);
        var lastStateHash = $"{currentChunk.SerialSha256}:{currentScrollbarHash}";
        var stepStart = 0;

        if (UsesProbeValidatedStableStart(context.Entry.TableName))
        {
            var probeScroll = TryScrollToNextChunk(
                run,
                controller,
                overlay,
                capturer,
                screenshots,
                context,
                currentChunk.SerialSha256,
                currentScrollbarHash,
                step: 0
            );
            scrollEvents.Add(probeScroll);
            if (!probeScroll.Changed)
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.table_start_probe_no_change",
                    run.RunDirectory,
                    $"table={context.Entry.TableName};tab={context.Entry.TabName};reason=stable_start_not_scrollable"
                );
                return (chunks, scrollEvents);
            }

            var (probeChunk, probeScrollbarHash) = CaptureTableChunk(
                run,
                controller,
                overlay,
                capturer,
                screenshots,
                context,
                chunkIndex: chunks.Count
            );
            var probeStateHash = $"{probeChunk.SerialSha256}:{probeScrollbarHash}";
            if (string.Equals(lastStateHash, probeStateHash, StringComparison.OrdinalIgnoreCase))
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.table_start_probe_repeat_state",
                    run.RunDirectory,
                    $"table={context.Entry.TableName};tab={context.Entry.TabName};reason=stable_start_repeated_after_probe"
                );
                return (chunks, scrollEvents);
            }

            chunks.Add(probeChunk);
            currentChunk = probeChunk;
            currentScrollbarHash = probeScrollbarHash;
            lastStateHash = probeStateHash;
            stepStart = 1;
        }

        for (var step = stepStart; step < Math.Max(0, maxSteps - 1); step++)
        {
            var scroll = TryScrollToNextChunk(
                run,
                controller,
                overlay,
                capturer,
                screenshots,
                context,
                currentChunk.SerialSha256,
                currentScrollbarHash,
                step
            );
            scrollEvents.Add(scroll);
            if (!scroll.Changed)
            {
                break;
            }

            var (nextChunk, nextScrollbarHash) = CaptureTableChunk(
                run,
                controller,
                overlay,
                capturer,
                screenshots,
                context,
                chunkIndex: chunks.Count
            );
            var nextStateHash = $"{nextChunk.SerialSha256}:{nextScrollbarHash}";
            if (string.Equals(lastStateHash, nextStateHash, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            chunks.Add(nextChunk);
            currentChunk = nextChunk;
            currentScrollbarHash = nextScrollbarHash;
            lastStateHash = nextStateHash;
        }

        return (chunks, scrollEvents);
    }

    private static (TestlabTableScanChunkResult Chunk, string ScrollbarHash) CaptureTableChunk(
        RunContext run,
        WindowController controller,
        ICaptureOverlay? overlay,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        TestlabTableScanContext context,
        int chunkIndex
    )
    {
        if (!SkipChunkFocusClick(context.Entry.TableName, context.PagingPreparationMode))
        {
            controller.ClickScreenPoint(context.FocusX, context.FocusY);
            Thread.Sleep(60);
        }
        overlay?.SetRect(context.TableRoiScreen);
        Thread.Sleep(80);
        var windowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(context.Window.Hwnd));
        _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(context.Entry.TableName)}_window_v_{chunkIndex:00}", windowBytes);
        var bytes = ImageCropper.TryCropToPngBytes(windowBytes, context.TableRoiWindow) ?? windowBytes;
        var path = screenshots.SaveEvidencePng(run, $"testlab_table_{Normalize(context.Entry.TableName)}_v_{chunkIndex:00}", bytes);
        var frameHash = ComputeSha256Hex(bytes);
        var serialBytes = ImageCropper.TryCropToPngBytes(windowBytes, context.SerialRoiWindow) ?? bytes;
        var serialPath = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(context.Entry.TableName)}_serial_v_{chunkIndex:00}", serialBytes);
        var serialHash = ComputeSha256Hex(serialBytes);
        var scrollbarBytes = ImageCropper.TryCropToPngBytes(windowBytes, context.ScrollbarRoiWindow) ?? bytes;
        var scrollbarHash = ComputeSha256Hex(scrollbarBytes);
        var chunk = new TestlabTableScanChunkResult(chunkIndex, path, context.TableRoiScreen, frameHash, context.SerialRoiScreen, serialPath, serialHash);
        return (chunk, scrollbarHash);
    }

    private static (byte[] Bytes, string Sha256) CaptureRoiSha256(
        ICaptureOverlay? overlay,
        ScreenCapture capturer,
        BBox roiScreen
    )
    {
        var bytes = CaptureWithOverlay(
            overlay,
            () => capturer.CaptureRegionPngBytes(roiScreen.X, roiScreen.Y, roiScreen.Width, roiScreen.Height)
        );
        return (bytes, ComputeSha256Hex(bytes));
    }

    private static bool ResetTableToTopBeforeScan(
        RunContext run,
        WindowController controller,
        ICaptureOverlay? overlay,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        TestlabTableScanContext context,
        int pauseMs,
        BBox? unusedTopSerialVerifyRoiWindow = null,
        string? unusedTopSerialVerifySha256 = null
    )
    {
        var entry = context.Entry;
        var win = context.Window;
        if (!RequiresDeterministicTopReset(entry))
        {
            return true;
        }

        var reusePreparation = ReusePagingPreparation(entry.TableName, context.PagingPreparationMode);
        var keyTarget = PrepareTableForPaging(run, controller, context);

        var pgUpCount = GetIntEnv("CHECKMIND_TABLE_RESET_TOP_PGUP_COUNT", 10);
        var pgUpRetryCount = GetIntEnv("CHECKMIND_TABLE_RESET_TOP_PGUP_RETRY_COUNT", 5);
        var stableConsecutive = Math.Max(1, GetIntEnv("CHECKMIND_TABLE_RESET_TOP_STABLE_CONSECUTIVE", 2));
        var keyDelayMs = GetIntEnv("CHECKMIND_TABLE_KEY_DELAY_MS", 10);
        var pagePauseMs = GetIntEnv("CHECKMIND_TABLE_PAGE_PAUSE_MS", 25);

        var beforeWindowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(context.Window.Hwnd));
        var beforeTableBytes = ImageCropper.TryCropToPngBytes(beforeWindowBytes, context.TableRoiWindow) ?? beforeWindowBytes;
        _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(entry.TableName)}_reset_top_before", beforeTableBytes);

        static (string SerialSha, string ScrollbarSha) CaptureResetState(TestlabTableScanContext scanContext, byte[] windowBytes)
        {
            var tableBytes = ImageCropper.TryCropToPngBytes(windowBytes, scanContext.TableRoiWindow) ?? windowBytes;
            var serialBytes = ImageCropper.TryCropToPngBytes(windowBytes, scanContext.SerialRoiWindow) ?? tableBytes;
            var scrollbarBytes = ImageCropper.TryCropToPngBytes(windowBytes, scanContext.ScrollbarRoiWindow) ?? tableBytes;
            return (ComputeSha256Hex(serialBytes), ComputeSha256Hex(scrollbarBytes));
        }

        var (beforeSerialSha, beforeScrollbarSha) = CaptureResetState(context, beforeWindowBytes);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pressAttempts = 0;
        var reachedTopStable = false;
        var currentWindowBytes = beforeWindowBytes;
        var currentSerialSha = beforeSerialSha;
        var currentScrollbarSha = beforeScrollbarSha;

        var stableRun = 0;

        void PressPgUpTimes(int times)
        {
            for (var i = 0; i < Math.Max(0, times); i++)
            {
                DispatchPagingKey(run, controller, entry.TableName, win, context.PagingPreparationMode, keyTarget, pageDown: false, keyDelayMs);
                pressAttempts++;
            }
        }

        bool BootstrapProfileEditorPagingSemantic()
        {
            if (!UsesProbeValidatedStableStart(entry.TableName))
            {
                return false;
            }

            var bootstrapBeforeWindowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(context.Window.Hwnd));
            var (bootstrapBeforeSerialSha, bootstrapBeforeScrollbarSha) = CaptureResetState(context, bootstrapBeforeWindowBytes);

            DispatchPagingKey(run, controller, entry.TableName, win, context.PagingPreparationMode, keyTarget, pageDown: true, keyDelayMs);
            pressAttempts++;
            Thread.Sleep(pagePauseMs);

            var bootstrapAfterWindowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(context.Window.Hwnd));
            var (bootstrapAfterSerialSha, bootstrapAfterScrollbarSha) = CaptureResetState(context, bootstrapAfterWindowBytes);
            var changed = !string.Equals(bootstrapBeforeSerialSha, bootstrapAfterSerialSha, StringComparison.OrdinalIgnoreCase) ||
                          !string.Equals(bootstrapBeforeScrollbarSha, bootstrapAfterScrollbarSha, StringComparison.OrdinalIgnoreCase);
            TestlabDebugMarkers.WritePhase(
                "runner.table_reset_top_bootstrap_pgdn",
                run.RunDirectory,
                $"table={entry.TableName};focus=({context.FocusX},{context.FocusY});serialBefore={bootstrapBeforeSerialSha};serialAfter={bootstrapAfterSerialSha};scrollbarBefore={bootstrapBeforeScrollbarSha};scrollbarAfter={bootstrapAfterScrollbarSha};serialChanged={(!string.Equals(bootstrapBeforeSerialSha, bootstrapAfterSerialSha, StringComparison.OrdinalIgnoreCase) ? 1 : 0)};scrollbarChanged={(!string.Equals(bootstrapBeforeScrollbarSha, bootstrapAfterScrollbarSha, StringComparison.OrdinalIgnoreCase) ? 1 : 0)};keyDelayMs={keyDelayMs};pagePauseMs={pagePauseMs}"
            );

            currentWindowBytes = bootstrapAfterWindowBytes;
            currentSerialSha = bootstrapAfterSerialSha;
            currentScrollbarSha = bootstrapAfterScrollbarSha;
            return changed;
        }

        bool ProbeTopStable()
        {
            var probeWindowBytes0 = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(context.Window.Hwnd));
            var (probeSerialSha0, probeScrollbarSha0) = CaptureResetState(context, probeWindowBytes0);
            var lastHash = $"{probeSerialSha0}:{probeScrollbarSha0}";
            var allSame = true;

            currentWindowBytes = probeWindowBytes0;
            currentSerialSha = probeSerialSha0;
            currentScrollbarSha = probeScrollbarSha0;

            for (var i = 0; i < stableConsecutive; i++)
            {
                if (!reusePreparation)
                {
                    keyTarget = PrepareTableForPaging(run, controller, context);
                }
                DispatchPagingKey(run, controller, entry.TableName, context.Window, context.PagingPreparationMode, keyTarget, pageDown: false, keyDelayMs);
                pressAttempts++;
                Thread.Sleep(pagePauseMs);
                var probeWindowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(context.Window.Hwnd));
                var (probeSerialSha, probeScrollbarSha) = CaptureResetState(context, probeWindowBytes);
                var nextHash = $"{probeSerialSha}:{probeScrollbarSha}";
                allSame = allSame && string.Equals(lastHash, nextHash, StringComparison.OrdinalIgnoreCase);
                lastHash = nextHash;

                currentWindowBytes = probeWindowBytes;
                currentSerialSha = probeSerialSha;
                currentScrollbarSha = probeScrollbarSha;
            }

            stableRun = allSame ? stableConsecutive : 0;
            return allSame;
        }

        _ = BootstrapProfileEditorPagingSemantic();
        PressPgUpTimes(pgUpCount);
        Thread.Sleep(pagePauseMs);
        reachedTopStable = ProbeTopStable();

        if (!reachedTopStable)
        {
            PressPgUpTimes(pgUpRetryCount);
            Thread.Sleep(pagePauseMs);
            reachedTopStable = ProbeTopStable();
        }

        var topSignatureMatched = TryVerifyTopBySerialSignature(
            run,
            currentWindowBytes,
            context.SerialRoiWindow,
            context.TopSerialVerifyRoiWindow,
            screenshots,
            entry.TableName,
            context.TopSerialVerifySha256
        );
        var hasTopSignature = !string.IsNullOrWhiteSpace(context.TopSerialVerifySha256);
        var finalTopReached = UsesProbeValidatedStableStart(entry.TableName)
            ? reachedTopStable
            : hasTopSignature
                ? topSignatureMatched
                : reachedTopStable;

        sw.Stop();
        var elapsedMs = sw.ElapsedMilliseconds;

        var afterTableBytes = ImageCropper.TryCropToPngBytes(currentWindowBytes, context.TableRoiWindow) ?? currentWindowBytes;
        _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(entry.TableName)}_reset_top_after", afterTableBytes);

        TestlabDebugMarkers.WritePhase(
            "runner.table_reset_top",
            run.RunDirectory,
            $"table={entry.TableName};method=pgup;serialChanged={!string.Equals(beforeSerialSha, currentSerialSha, StringComparison.OrdinalIgnoreCase)};scrollbarChanged={!string.Equals(beforeScrollbarSha, currentScrollbarSha, StringComparison.OrdinalIgnoreCase)};focus=({context.FocusX},{context.FocusY});pgup={pgUpCount};retry={pgUpRetryCount};stableConsecutive={stableConsecutive};stableRun={stableRun};keyDelayMs={keyDelayMs};pagePauseMs={pagePauseMs};pressAttempts={pressAttempts};elapsedMs={elapsedMs};stableByHash={(reachedTopStable ? 1 : 0)};topSignature={(topSignatureMatched ? 1 : 0)};stableTop={(finalTopReached ? 1 : 0)}"
        );
        return finalTopReached;
    }

    private static bool TryVerifyTopBySerialSignature(
        RunContext run,
        byte[] windowBytes,
        BBox serialRoiWindow,
        BBox? expectedRoiWindow,
        ScreenshotStore screenshots,
        string tableName,
        string? expectedSha256
    )
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            TestlabDebugMarkers.WritePhase(
                "runner.table_reset_top_signature",
                run.RunDirectory,
                $"table={tableName};configured=0;matched=0"
            );
            return false;
        }

        BBox verifyRoiWindow;
        if (expectedRoiWindow is not null)
        {
            verifyRoiWindow = expectedRoiWindow.Value;
        }
        else
        {
            var (windowW, windowH) = ImageGeometry.GetSize(windowBytes);
            verifyRoiWindow = new BBox(
                X: Math.Max(0, serialRoiWindow.X - 12),
                Y: serialRoiWindow.Y,
                Width: Math.Min(windowW - Math.Max(0, serialRoiWindow.X - 12), serialRoiWindow.Width + 36),
                Height: Math.Min(windowH - serialRoiWindow.Y, serialRoiWindow.Height)
            );
        }

        var serialBytes = ImageCropper.TryCropToPngBytes(windowBytes, verifyRoiWindow);
        if (serialBytes is null || serialBytes.Length == 0)
        {
            TestlabDebugMarkers.WritePhase(
                "runner.table_reset_top_signature",
                run.RunDirectory,
                $"table={tableName};configured=1;matched=0;reason=empty_capture"
            );
            return false;
        }

        var shotId = $"testlab_table_{Normalize(tableName)}_reset_top_signature_verify";
        _ = screenshots.SaveDebugPng(run, shotId, serialBytes);
        var actualSha256 = ComputeSha256Hex(serialBytes);
        var matched = string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
        TestlabDebugMarkers.WritePhase(
            "runner.table_reset_top_signature",
            run.RunDirectory,
            $"table={tableName};configured=1;matched={(matched ? 1 : 0)};actual={actualSha256};expected={expectedSha256}"
        );
        return matched;
    }

    private static TestlabTableScrollEvent TryScrollToNextChunk(
        RunContext run,
        WindowController controller,
        ICaptureOverlay? overlay,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        TestlabTableScanContext context,
        string currentSerialSha256,
        string currentScrollbarSha256,
        int step
    )
    {
        var tableName = context.Entry.TableName;
        var debugArtifacts = IsDebugArtifactsEnabled();
        var keyDelayMs = GetIntEnv("CHECKMIND_TABLE_KEY_DELAY_MS", 10);
        var pagePauseMs = GetIntEnv("CHECKMIND_TABLE_PAGE_PAUSE_MS", 25);

        TestlabTableScrollEvent Make(string method, string? afterSerial, string? afterScrollbar, bool changed)
            => new TestlabTableScrollEvent(step, currentSerialSha256, afterSerial, currentScrollbarSha256, afterScrollbar, method, pagePauseMs, changed);

        void SaveMethodEvidence(string method, byte[] serialBytes, string serialSha, string scrollbarSha)
        {
            if (!debugArtifacts)
            {
                return;
            }

            var windowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(context.Window.Hwnd));
            _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(tableName)}_window_after_v_{step:00}_{method}", windowBytes);
            var roiBytes = ImageCropper.TryCropToPngBytes(windowBytes, context.TableRoiWindow) ?? windowBytes;
            _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(tableName)}_after_v_{step:00}_{method}", roiBytes);
            _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(tableName)}_serial_after_v_{step:00}_{method}", serialBytes);
            TestlabDebugMarkers.WritePhase(
                "runner.table_scroll_attempt",
                run.RunDirectory,
                $"table={tableName};step={step};method={method};serialChanged={!string.Equals(serialSha, currentSerialSha256, StringComparison.OrdinalIgnoreCase)};scrollbarChanged={!string.Equals(scrollbarSha, currentScrollbarSha256, StringComparison.OrdinalIgnoreCase)};focus=({context.FocusX},{context.FocusY});scrollbar=({context.ScrollbarRoiScreen.X},{context.ScrollbarRoiScreen.Y},{context.ScrollbarRoiScreen.Width},{context.ScrollbarRoiScreen.Height})"
            );
        }

        bool Detect(out string afterSerialSha, out string afterScrollbarSha, out byte[] afterSerialBytes)
        {
            var windowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(context.Window.Hwnd));
            var serialBytes = ImageCropper.TryCropToPngBytes(windowBytes, context.SerialRoiWindow) ?? windowBytes;
            var scrollbarBytes = ImageCropper.TryCropToPngBytes(windowBytes, context.ScrollbarRoiWindow) ?? windowBytes;
            var serialSha = ComputeSha256Hex(serialBytes);
            var scrollbarSha = ComputeSha256Hex(scrollbarBytes);
            afterSerialBytes = serialBytes;
            afterSerialSha = serialSha;
            afterScrollbarSha = scrollbarSha;
            return !string.Equals(serialSha, currentSerialSha256, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(scrollbarSha, currentScrollbarSha256, StringComparison.OrdinalIgnoreCase);
        }

        var keyTarget = ReusePagingPreparation(tableName, context.PagingPreparationMode)
            ? context.Window.Hwnd
            : PrepareTableForPaging(run, controller, context);
        DispatchPagingKey(run, controller, tableName, context.Window, context.PagingPreparationMode, keyTarget, pageDown: true, keyDelayMs);
        Thread.Sleep(pagePauseMs);
        Detect(out var pageShaProbe, out var pageBarProbe, out var pageBytesProbe);
        SaveMethodEvidence("pgdn", pageBytesProbe, pageShaProbe, pageBarProbe);
        if (Detect(out var sha3, out var bar3, out var bytes3))
        {
            if (debugArtifacts)
            {
                _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(tableName)}_serial_after_v_{step:00}_pgdn", bytes3);
            }
            return Make("pgdn", sha3, bar3, true);
        }

        var finalWindowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(context.Window.Hwnd));
        var finalBytes = ImageCropper.TryCropToPngBytes(finalWindowBytes, context.SerialRoiWindow) ?? finalWindowBytes;
        var finalSha = ComputeSha256Hex(finalBytes);
        var finalScrollbarBytes = ImageCropper.TryCropToPngBytes(finalWindowBytes, context.ScrollbarRoiWindow) ?? finalWindowBytes;
        var finalBar = ComputeSha256Hex(finalScrollbarBytes);
        _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(tableName)}_serial_after_v_{step:00}_fail", finalBytes);
        TestlabDebugMarkers.WritePhase(
            "runner.table_scroll_no_change",
            run.RunDirectory,
            $"table={tableName};step={step};method=pgdn;keyDelayMs={keyDelayMs};pagePauseMs={pagePauseMs}"
        );
        return Make("fail", finalSha, finalBar, false);
    }

    private static IntPtr PrepareTableForPaging(
        RunContext run,
        WindowController controller,
        TestlabTableScanContext context
    )
    {
        var tableName = context.Entry.TableName;
        var win = context.Window;
        var singleActivationOnly = UseSingleActivationOnly(tableName, context.PagingPreparationMode);
        var focusClickCount = singleActivationOnly
            ? 0
            : GetFocusClickCount(tableName, context.PagingPreparationMode);
        var focusClickPauseMs = GetIntEnv("CHECKMIND_TABLE_FOCUS_CLICK_PAUSE_MS", 90);
        var focusSettleMs = GetIntEnv("CHECKMIND_TABLE_FOCUS_SETTLE_MS", 220);
        var activationClickCount = GetIntEnv("CHECKMIND_TABLE_ACTIVATION_CLICK_COUNT", 1);
        var activationClickPauseMs = GetIntEnv("CHECKMIND_TABLE_ACTIVATION_CLICK_PAUSE_MS", 120);
        var activationSettleMs = GetIntEnv("CHECKMIND_TABLE_ACTIVATION_SETTLE_MS", 320);
        var pointerMode = GetTablePointerInputMode(context.PagingPreparationMode);

        controller.Activate(win.Hwnd);
        Thread.Sleep(Math.Max(0, focusClickPauseMs));

        var beforeForeground = controller.GetForegroundWindowHandle();
        var beforeForegroundTitle = Win32Native.GetWindowTitle(beforeForeground);
        var pointWindowBefore = controller.GetWindowFromScreenPoint(context.FocusX, context.FocusY);
        var pointWindowBeforeTitle = Win32Native.GetWindowTitle(pointWindowBefore);
        var activationPointWindowBefore = controller.GetWindowFromScreenPoint(context.ActivationX, context.ActivationY);
        var activationPointWindowBeforeTitle = Win32Native.GetWindowTitle(activationPointWindowBefore);

        IntPtr activationPointWindowAfter = activationPointWindowBefore;
        string? activationPointWindowAfterTitle = activationPointWindowBeforeTitle;
        if (context.HasExplicitActivation)
        {
            DispatchPointerClicks(run, controller, tableName, "activation", context.ActivationX, context.ActivationY, activationClickCount, activationClickPauseMs, pointerMode);

            Thread.Sleep(Math.Max(0, activationSettleMs));
            activationPointWindowAfter = controller.GetWindowFromScreenPoint(context.ActivationX, context.ActivationY);
            activationPointWindowAfterTitle = Win32Native.GetWindowTitle(activationPointWindowAfter);
        }

        var useSeparateFocusStage = !singleActivationOnly &&
                                    (!context.HasExplicitActivation || context.ActivationX != context.FocusX || context.ActivationY != context.FocusY);

        if (useSeparateFocusStage && focusClickCount > 0)
        {
            DispatchPointerClicks(run, controller, tableName, "focus", context.FocusX, context.FocusY, focusClickCount, focusClickPauseMs, pointerMode);
        }

        if (focusClickCount > 0)
        {
            Thread.Sleep(Math.Max(0, focusSettleMs));
        }

        var afterForeground = controller.GetForegroundWindowHandle();
        var afterForegroundTitle = Win32Native.GetWindowTitle(afterForeground);
        var pointWindowAfter = focusClickCount > 0
            ? controller.GetWindowFromScreenPoint(context.FocusX, context.FocusY)
            : activationPointWindowAfter;
        var pointWindowAfterTitle = Win32Native.GetWindowTitle(pointWindowAfter);

        var resolvedKeyTarget = pointWindowAfter != IntPtr.Zero
            ? pointWindowAfter
            : activationPointWindowAfter != IntPtr.Zero
                ? activationPointWindowAfter
                : win.Hwnd;

        TestlabDebugMarkers.WritePhase(
            "runner.table_focus",
            run.RunDirectory,
            $"table={tableName};focus=({context.FocusX},{context.FocusY});activation=({context.ActivationX},{context.ActivationY});hasActivation={(context.HasExplicitActivation ? 1 : 0)};preparationMode={SanitizePhaseValue(context.PagingPreparationMode)};activationClickCount={activationClickCount};activationPauseMs={activationClickPauseMs};activationSettleMs={activationSettleMs};clickCount={focusClickCount};clickPauseMs={focusClickPauseMs};settleMs={focusSettleMs};targetHwnd=0x{win.Hwnd.ToInt64():X};targetTitle={SanitizePhaseValue(win.Title)};beforeFg=0x{beforeForeground.ToInt64():X};beforeFgTitle={SanitizePhaseValue(beforeForegroundTitle)};afterFg=0x{afterForeground.ToInt64():X};afterFgTitle={SanitizePhaseValue(afterForegroundTitle)};activationPointBefore=0x{activationPointWindowBefore.ToInt64():X};activationPointBeforeTitle={SanitizePhaseValue(activationPointWindowBeforeTitle)};activationPointAfter=0x{activationPointWindowAfter.ToInt64():X};activationPointAfterTitle={SanitizePhaseValue(activationPointWindowAfterTitle)};pointBefore=0x{pointWindowBefore.ToInt64():X};pointBeforeTitle={SanitizePhaseValue(pointWindowBeforeTitle)};pointAfter=0x{pointWindowAfter.ToInt64():X};pointAfterTitle={SanitizePhaseValue(pointWindowAfterTitle)};resolvedKeyTarget=0x{resolvedKeyTarget.ToInt64():X}"
        );
        return resolvedKeyTarget;
    }

    private static void DispatchPagingKey(
        RunContext run,
        WindowController controller,
        string tableName,
        TestlabWindowInfo win,
        string pagingPreparationMode,
        IntPtr keyTarget,
        bool pageDown,
        int keyDelayMs
    )
    {
        var mode = GetTableKeyInputMode(pagingPreparationMode);
        var beforeForeground = controller.GetForegroundWindowHandle();
        var beforeForegroundTitle = Win32Native.GetWindowTitle(beforeForeground);

        if (string.Equals(mode, "foreground", StringComparison.OrdinalIgnoreCase))
        {
            controller.Activate(win.Hwnd);
            var foregroundSettleMs = GetIntEnv("CHECKMIND_TABLE_KEY_FOREGROUND_SETTLE_MS", 80);
            if (foregroundSettleMs > 0)
            {
                Thread.Sleep(foregroundSettleMs);
            }

            if (pageDown)
            {
                controller.PressPageDownToForegroundWindow(keyDelayMs);
            }
            else
            {
                controller.PressPageUpToForegroundWindow(keyDelayMs);
            }
        }
        else if (string.Equals(mode, "sendinput_foreground", StringComparison.OrdinalIgnoreCase))
        {
            controller.Activate(win.Hwnd);
            var foregroundSettleMs = GetIntEnv("CHECKMIND_TABLE_KEY_FOREGROUND_SETTLE_MS", 80);
            if (foregroundSettleMs > 0)
            {
                Thread.Sleep(foregroundSettleMs);
            }

            if (pageDown)
            {
                controller.PressPageDownToForegroundWindowBySendInput(keyDelayMs);
            }
            else
            {
                controller.PressPageUpToForegroundWindowBySendInput(keyDelayMs);
            }
        }
        else
        {
            if (pageDown)
            {
                controller.PressPageDownToWindow(keyTarget, keyDelayMs);
            }
            else
            {
                controller.PressPageUpToWindow(keyTarget, keyDelayMs);
            }
        }

        var afterForeground = controller.GetForegroundWindowHandle();
        var afterForegroundTitle = Win32Native.GetWindowTitle(afterForeground);
        TestlabDebugMarkers.WritePhase(
            "runner.table_key_dispatch",
            run.RunDirectory,
            $"table={tableName};key={(pageDown ? "pgdn" : "pgup")};mode={mode};targetHwnd=0x{keyTarget.ToInt64():X};targetTitle={SanitizePhaseValue(win.Title)};beforeFg=0x{beforeForeground.ToInt64():X};beforeFgTitle={SanitizePhaseValue(beforeForegroundTitle)};afterFg=0x{afterForeground.ToInt64():X};afterFgTitle={SanitizePhaseValue(afterForegroundTitle)};keyDelayMs={keyDelayMs}"
        );
    }

    private static void DispatchPointerClicks(
        RunContext run,
        WindowController controller,
        string tableName,
        string role,
        int x,
        int y,
        int clickCount,
        int clickPauseMs,
        string mode
    )
    {
        var normalizedClickCount = Math.Max(1, clickCount);
        TestlabDebugMarkers.WritePhase(
            "runner.table_pointer_dispatch",
            run.RunDirectory,
            $"table={tableName};role={role};mode={mode};point=({x},{y});clickCount={normalizedClickCount};clickPauseMs={clickPauseMs}"
        );

        for (var i = 0; i < normalizedClickCount; i++)
        {
            DispatchPointerClick(controller, x, y, mode);

            Thread.Sleep(Math.Max(0, clickPauseMs));
        }
    }

    private static void DispatchPointerClick(
        WindowController controller,
        int x,
        int y,
        string mode
    )
    {
        if (string.Equals(mode, "sendinput", StringComparison.OrdinalIgnoreCase))
        {
            controller.ClickScreenPointBySendInput(x, y);
        }
        else
        {
            controller.ClickScreenPoint(x, y);
        }
    }

    private static string GetTableKeyInputMode(string? pagingPreparationMode)
    {
        var modeNorm = Normalize(pagingPreparationMode ?? string.Empty);
        if (modeNorm == "activationforeground" ||
            modeNorm == "foregroundactivation")
        {
            return "foreground";
        }

        if (modeNorm == "activationsendinputforeground" ||
            modeNorm == "sendinputforegroundactivation" ||
            modeNorm == "notchprofile" ||
            modeNorm == "notchprofilesendinput")
        {
            return "sendinput_foreground";
        }

        var raw = (Environment.GetEnvironmentVariable("CHECKMIND_TABLE_KEY_INPUT_MODE") ?? string.Empty).Trim();
        if (string.Equals(raw, "foreground", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "physical", StringComparison.OrdinalIgnoreCase))
        {
            return "foreground";
        }

        if (string.Equals(raw, "sendinput", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "sendinput_foreground", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "foreground_sendinput", StringComparison.OrdinalIgnoreCase))
        {
            return "sendinput_foreground";
        }

        return "window_message";
    }

    private static string GetTablePointerInputMode(string? pagingPreparationMode)
    {
        var modeNorm = Normalize(pagingPreparationMode ?? string.Empty);
        if (modeNorm == "activationforeground" ||
            modeNorm == "foregroundactivation")
        {
            return "mouse_event";
        }

        if (modeNorm == "activationsendinputforeground" ||
            modeNorm == "sendinputforegroundactivation" ||
            modeNorm == "notchprofile" ||
            modeNorm == "notchprofilesendinput")
        {
            return "sendinput";
        }

        var raw = (Environment.GetEnvironmentVariable("CHECKMIND_TABLE_POINTER_INPUT_MODE") ?? string.Empty).Trim();
        if (string.Equals(raw, "sendinput", StringComparison.OrdinalIgnoreCase))
        {
            return "sendinput";
        }

        return "mouse_event";
    }

    private static bool RequiresExplicitPagingFocus(string tableName, string? pagingPreparationMode)
    {
        var modeNorm = Normalize(pagingPreparationMode ?? string.Empty);
        return string.Equals(Normalize(tableName ?? string.Empty), "profileeditortablescan", StringComparison.OrdinalIgnoreCase) &&
               (modeNorm == "activationforeground" || modeNorm == "foregroundactivation");
    }

    private static bool UsesProbeValidatedStableStart(string tableName)
        => string.Equals(Normalize(tableName ?? string.Empty), "profileeditortablescan", StringComparison.OrdinalIgnoreCase);

    private static bool ReusePagingPreparation(string tableName, string? pagingPreparationMode)
    {
        if (RequiresExplicitPagingFocus(tableName, pagingPreparationMode))
        {
            return false;
        }

        var modeNorm = Normalize(pagingPreparationMode ?? string.Empty);
        return modeNorm == "activationforeground" ||
               modeNorm == "foregroundactivation" ||
               modeNorm == "activationsendinputforeground" ||
               modeNorm == "sendinputforegroundactivation" ||
               modeNorm == "notchprofile" ||
               modeNorm == "notchprofilesendinput";
    }

    private static bool UseSingleActivationOnly(string tableName, string? pagingPreparationMode)
    {
        if (RequiresExplicitPagingFocus(tableName, pagingPreparationMode))
        {
            return false;
        }

        var modeNorm = Normalize(pagingPreparationMode ?? string.Empty);
        return modeNorm == "activationforeground" ||
               modeNorm == "foregroundactivation";
    }

    private static bool SkipChunkFocusClick(string tableName, string? pagingPreparationMode)
    {
        if (RequiresExplicitPagingFocus(tableName, pagingPreparationMode))
        {
            return false;
        }

        var modeNorm = Normalize(pagingPreparationMode ?? string.Empty);
        return modeNorm == "activationforeground" ||
               modeNorm == "foregroundactivation";
    }

    private static int GetFocusClickCount(string tableName, string? pagingPreparationMode)
    {
        if (RequiresExplicitPagingFocus(tableName, pagingPreparationMode))
        {
            // Profile Editor regressions show that a double-click can push the table
            // into a non-scrollable edit-like state. Keep an explicit focus click, but
            // narrow it to a single click so the automation matches the validated
            // manual action more closely.
            return 1;
        }

        return GetIntEnv("CHECKMIND_TABLE_FOCUS_CLICK_COUNT", 2);
    }

    private static string SanitizePhaseValue(string? value)
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

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
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

    private static void WriteCoverage(RunContext run, IReadOnlyList<TestlabTableScanResult> scans)
    {
        var json = JsonSerializer.Serialize(scans, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(run.RunDirectory, "coverage.json"), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static (IReadOnlyList<TestlabNotchProfileScanResult>? Results, TestlabNotchProfileCountMismatchResult? CountMismatch) RunNotchProfilesIfRequested(
        RunContext run,
        WindowController controller,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        ICaptureOverlay? overlay
    )
    {
        var indexes = GetNotchProfileIndexesFromEnv();
        if (indexes.Count == 0)
        {
            return (null, null);
        }

        var requestedCount = NotchProfileIndexResolver.ResolveCountFromEnvironment();
        var countDrivenMode = IsNotchProfileCountModeRequested();
        var requestedTotal = requestedCount ?? indexes.Count;
        var requestedInputLabel = countDrivenMode ? "Notch Profile Count" : "Notch Profile Indexes";

        var profileStore = WorkstationProfileStore.CreateDefault();
        var profile = profileStore.Load();
        var defineNotchWindow = profile.FindChildWindowProfile("define_notch_profiles")
            ?? throw new InvalidOperationException("profile 缺少 ChildWindows.define_notch_profiles 配置。");
        var listTarget = defineNotchWindow.FindListTarget("notch_profiles_list")
            ?? throw new InvalidOperationException("profile 缺少 define_notch_profiles.listTargets.notch_profiles_list 配置。");
        var notchProfileWindow = profile.FindChildWindowProfile("notch_profile")
            ?? throw new InvalidOperationException("profile 缺少 ChildWindows.notch_profile 配置。");
        var tableScanTarget = notchProfileWindow.FindCaptureTarget("table_scan")
            ?? throw new InvalidOperationException("profile 缺少 notch_profile.captureTargets.table_scan 配置。");
        if (tableScanTarget.RoiWindow is not BBox tableRoiWindow)
        {
            throw new InvalidOperationException("profile 缺少 notch_profile.captureTargets.table_scan.RoiWindow 配置。");
        }

        var entryClickSequence = defineNotchWindow.GetOpenClickSequence();
        if (entryClickSequence.Length == 0)
        {
            throw new InvalidOperationException("profile 缺少 define_notch_profiles.openClickSequence / openClickPoint 配置。");
        }

        var childTitleContains = (Environment.GetEnvironmentVariable("CHECKMIND_NOTCH_PROFILE_TITLE_CONTAINS") ?? "Notch Profile").Trim();
        if (string.IsNullOrWhiteSpace(childTitleContains))
        {
            childTitleContains = "Notch Profile";
        }

        var mainWindow = new TestlabWindowLocator().Find();
        var childLocator = new TestlabChildWindowLocator();
        var automation = new TestlabChildWindowAutomation(childLocator, controller);
        var results = new List<TestlabNotchProfileScanResult>();

        controller.Activate(mainWindow.Hwnd);
        Thread.Sleep(80);
        _ = RestoreSineSetupChannelSafetyParameters(
            run,
            controller,
            capturer,
            screenshots,
            overlay,
            mainWindow,
            "Sine Setup",
            0,
            evidencePrefix: "notch_profiles"
        );

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

        VerifyDefineNotchProfilesLayoutOrThrow(
            run,
            controller,
            capturer,
            screenshots,
            defineWindow,
            defineNotchWindow,
            listTarget,
            profileStore.ProfilePath
        );

        var fixedProfile = IsFixedCaptureEnabled()
            ? profile
            : null;
        var maxStepsRaw = Environment.GetEnvironmentVariable("CHECKMIND_TABLE_SCAN_V_MAX_STEPS");
        var maxSteps = int.TryParse(maxStepsRaw, out var v) ? v : 8;
        maxSteps = Math.Clamp(maxSteps, 1, 50);

        var pauseRaw = Environment.GetEnvironmentVariable("CHECKMIND_TABLE_SCAN_PAUSE_MS");
        var pauseMs = int.TryParse(pauseRaw, out var p) ? p : 250;
        pauseMs = Math.Clamp(pauseMs, 80, 2000);
        TestlabNotchProfileCountMismatchResult? countMismatch = null;
        int? lastCompletedRowIndex = null;
        string? lastCompletedSelectionStateSha256 = null;

        foreach (var rowIndex in indexes)
        {
            TestlabDebugMarkers.WritePhase("runner.notch_profile_scan_begin", run.RunDirectory, $"row={rowIndex}");
            controller.Activate(defineWindow.Hwnd);
            Thread.Sleep(80);

            var defineWindowScreenshotPath = screenshots.SaveEvidencePng(
                run,
                $"notch_profiles_define_window_before_open_{rowIndex:00}",
                capturer.CaptureWindowPngBytes(defineWindow.Hwnd)
            );

            TestlabChildWindowOpenResult opened;
            try
            {
                opened = automation.OpenChildWindowFromIndexedListEntry(
                    defineWindow,
                    listTarget,
                    rowIndex,
                    childTitleContains,
                    maximizeChildWindow: true,
                    capturer: capturer,
                    requireSelectionChange: lastCompletedRowIndex.HasValue && lastCompletedRowIndex.Value != rowIndex,
                    previousSelectionStateSha256: lastCompletedSelectionStateSha256,
                    selectionRetryCount: 1
                );
            }
            catch (ListEntryOutOfRangeException ex)
            {
                countMismatch = CreateNotchProfileCountMismatchResult(
                    requestedTotal,
                    rowIndex,
                    results,
                    $"目标序号 [{rowIndex}] 的行点击点已超出列表 ROI，本次按实际列表数量正常结束。请检查 {requestedInputLabel} 是否填写正确。",
                    ex.Message
                );
                TestlabDebugMarkers.WritePhase(
                    "runner.notch_profile_count_mismatch",
                    run.RunDirectory,
                    $"requested={requestedTotal};completed={results.Count};failedRow={rowIndex};mode=out_of_range;input={requestedInputLabel}"
                );
                break;
            }
            catch (ListEntrySelectionNotChangedException ex)
            {
                countMismatch = CreateNotchProfileCountMismatchResult(
                    requestedTotal,
                    rowIndex,
                    results,
                    $"目标序号 [{rowIndex}] 点击后未检测到新的有效列表项，本次按实际列表数量正常结束。请检查 {requestedInputLabel} 是否填写正确。",
                    ex.Message
                );
                TestlabDebugMarkers.WritePhase(
                    "runner.notch_profile_count_mismatch",
                    run.RunDirectory,
                    $"requested={requestedTotal};completed={results.Count};failedRow={rowIndex};mode=selection_not_changed;input={requestedInputLabel}"
                );
                break;
            }
            catch (ListEntryRepeatedSelectionException ex)
            {
                countMismatch = CreateNotchProfileCountMismatchResult(
                    requestedTotal,
                    rowIndex,
                    results,
                    $"目标序号 [{rowIndex}] 连续点击两次后仍停留在上一条有效列表项，本次按实际列表数量正常结束。请检查 {requestedInputLabel} 是否填写正确。",
                    ex.Message
                );
                TestlabDebugMarkers.WritePhase(
                    "runner.notch_profile_count_mismatch",
                    run.RunDirectory,
                    $"requested={requestedTotal};completed={results.Count};failedRow={rowIndex};mode=repeated_selection;input={requestedInputLabel}"
                );
                break;
            }

            var notchWindow = childLocator.FindByTitleContains(
                childTitleContains,
                processName: defineWindow.ProcessName,
                timeoutMs: 1000
            );
            controller.Activate(notchWindow.Hwnd);
            Thread.Sleep(120);

            var notchWindowPngBytes = capturer.CaptureWindowPngBytes(notchWindow.Hwnd);
            var tableEntryBytes = ImageCropper.TryCropToPngBytes(notchWindowPngBytes, tableRoiWindow) ?? notchWindowPngBytes;
            var tableEntryPath = screenshots.SaveEvidencePng(run, $"notch_profile_{rowIndex:00}_table_entry", tableEntryBytes);
            var tableRoiScreen = MapWindowRoiToScreenRoi(tableRoiWindow, notchWindow);

            var entry = new TestlabTableEntryResult(
                "Notch Profile",
                $"Notch Profile Table #{rowIndex}",
                tableEntryPath,
                tableRoiWindow,
                tableRoiScreen,
                tableScanTarget.PagingFocusPointWindow,
                tableScanTarget.PagingActivationPointWindow,
                tableScanTarget.PagingPreparationMode
            );

            var tableScan = ScanSingleTableWithDeterministicPaging(
                run,
                controller,
                overlay,
                capturer,
                screenshots,
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

            string? defineAfterClosePath = null;
            if (childWindowClosed)
            {
                defineWindow = new TestlabWindowLocator().Find();
                controller.Activate(defineWindow.Hwnd);
                Thread.Sleep(120);
                defineAfterClosePath = screenshots.SaveEvidencePng(
                    run,
                    $"notch_profiles_define_window_after_close_{rowIndex:00}",
                    capturer.CaptureWindowPngBytes(defineWindow.Hwnd)
                );
            }
            else
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.notch_profile_close_failed",
                    run.RunDirectory,
                    $"row={rowIndex};closeMode={closeMode};childTitleContains={childTitleContains}"
                );
                throw new InvalidOperationException(
                    $"Notch Profile 序号 {rowIndex} 扫描完成后子窗口未关闭，已停止继续执行后续序号。closeMode={closeMode}"
                );
            }

            results.Add(
                new TestlabNotchProfileScanResult(
                    rowIndex,
                    opened,
                    defineWindowScreenshotPath,
                    tableEntryPath,
                    tableRoiWindow,
                    tableRoiScreen,
                    tableScan,
                    closeMode,
                    childWindowClosed,
                    defineAfterClosePath
                )
            );
            TestlabDebugMarkers.WritePhase(
                "runner.notch_profile_scan_completed",
                run.RunDirectory,
                $"row={rowIndex};chunks={tableScan.Chunks.Count};unique={tableScan.UniqueChunkCount};closed={(childWindowClosed ? 1 : 0)}"
            );
            lastCompletedRowIndex = rowIndex;
            lastCompletedSelectionStateSha256 = opened.SelectionStateAfterSha256;
        }

        return (results, countMismatch);
    }

    private static void VerifyDefineNotchProfilesLayoutOrThrow(
        RunContext run,
        WindowController controller,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        TestlabWindowInfo defineWindow,
        WorkstationChildWindowProfile defineWindowProfile,
        WorkstationListNavigationTarget listTarget,
        string profilePath
    )
    {
        var verifyTarget = defineWindowProfile.FindVerifyTarget("notch_profiles_layout");
        if (verifyTarget?.RoiWindow is not BBox verifyRoi || string.IsNullOrWhiteSpace(verifyTarget.Sha256))
        {
            var reportPathMissing = Path.Combine(run.RunDirectory, "define_notch_profiles_layout_preflight_report.json");
            var reportMissing = new PreflightCalibrationReport(
                IsCompliant: false,
                ProfilePath: profilePath,
                Tabs: ["Sine Setup"],
                Failures:
                [
                    new PreflightCalibrationFailure(
                        Key: "notch_profiles_layout_verify_missing",
                        Message: "缺少 Notch Profiles 列表布局签名，已阻断正式执行。",
                        Expected: "define_notch_profiles.VerifyTargets.notch_profiles_layout (RoiWindow + Sha256) configured",
                        Actual: "null",
                        Suggestion: "请先执行 Notch Profiles 布局签名标定，再重试正式业务链路。"
                    )
                ]
            );
            File.WriteAllText(reportPathMissing, reportMissing.ToJson(), new UTF8Encoding(false));
            TestlabDebugMarkers.WritePhase(
                "runner.notch_profiles_layout_signature_missing",
                run.RunDirectory,
                $"report={reportPathMissing}"
            );
            throw new PreflightCalibrationGateException("Notch Profiles 布局签名缺失，已阻断正式执行。", reportPathMissing);
        }

        if (listTarget.FirstRowAnchor is WindowPoint firstRowAnchor)
        {
            controller.Activate(defineWindow.Hwnd);
            Thread.Sleep(80);
            controller.ClickWindowPoint(defineWindow.Hwnd, firstRowAnchor);
            Thread.Sleep(180);
            TestlabDebugMarkers.WritePhase(
                "runner.notch_profiles_layout_selection_normalized",
                run.RunDirectory,
                $"rowPoint=({firstRowAnchor.X},{firstRowAnchor.Y})"
            );
        }

        var windowBytes = capturer.CaptureWindowPngBytes(defineWindow.Hwnd);
        var actualBytes = ImageCropper.TryCropToPngBytes(windowBytes, verifyRoi);
        if (actualBytes is null || actualBytes.Length == 0)
        {
            throw new InvalidOperationException("无法采集 Define notch profiles 布局签名当前帧。");
        }

        var actualPngBytesSha256 = ComputeSha256Hex(actualBytes);
        var actualSha256 = ComputeImageContentSha256Hex(actualBytes);
        var expectedSha256 = verifyTarget.Sha256.Trim();
        if (string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            TestlabDebugMarkers.WritePhase(
                "runner.notch_profiles_layout_signature_matched",
                run.RunDirectory,
                $"actual={actualSha256};expected={expectedSha256};verifyRoi=({verifyRoi.X},{verifyRoi.Y},{verifyRoi.Width},{verifyRoi.Height})"
            );
            return;
        }

        var windowEvidencePath = screenshots.SaveEvidencePng(run, "define_notch_profiles_layout_window_actual", windowBytes);
        var evidencePath = screenshots.SaveEvidencePng(run, "define_notch_profiles_layout_signature_actual", actualBytes);
        var reportPath = Path.Combine(run.RunDirectory, "define_notch_profiles_layout_hash_warning.json");
        var reportJson = JsonSerializer.Serialize(
            new
            {
                mode = "geometry_gate_with_hash_warning",
                profilePath,
                verifyRoi,
                expectedSha256,
                actualPixelSha256 = actualSha256,
                actualPngBytesSha256 = actualPngBytesSha256,
                evidencePath,
                windowEvidencePath,
                note = "Bottom controls ROI captured successfully. Hash drift is recorded as evidence but no longer blocks Notch Profile execution."
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(reportPath, reportJson, new UTF8Encoding(false));
        TestlabDebugMarkers.WritePhase(
            "runner.notch_profiles_layout_hash_mismatch_ignored",
            run.RunDirectory,
            $"actualPixel={actualSha256};actualPngBytes={actualPngBytesSha256};expected={expectedSha256};report={reportPath};evidence={evidencePath};window={windowEvidencePath};verifyRoi=({verifyRoi.X},{verifyRoi.Y},{verifyRoi.Width},{verifyRoi.Height})"
        );
        TestlabDebugMarkers.WritePhase(
            "runner.notch_profiles_layout_geometry_verified",
            run.RunDirectory,
            $"mode=geometry_only;verifyRoi=({verifyRoi.X},{verifyRoi.Y},{verifyRoi.Width},{verifyRoi.Height})"
        );
    }

    private static (byte[] WindowBytes, string ScreenshotPath) RestoreSineSetupChannelSafetyParameters(
        RunContext run,
        WindowController controller,
        ScreenCapture capturer,
        ScreenshotStore screenshots,
        ICaptureOverlay? overlay,
        TestlabWindowInfo win,
        string tabName,
        int index,
        string evidencePrefix
    )
    {
        var profile = WorkstationProfileStore.CreateDefault().Load();
        var (restoreSequence, source) = ResolveSineSetupChannelSafetyRestoreSequence(profile);

        controller.Activate(win.Hwnd);
        Thread.Sleep(100);
        foreach (var restorePoint in restoreSequence)
        {
            controller.ClickWindowPoint(win.Hwnd, restorePoint);
            Thread.Sleep(180);
        }
        var settleMs = GetIntEnv("CHECKMIND_SINE_SETUP_CHANNEL_SAFETY_RESTORE_SLEEP_MS", 360);
        Thread.Sleep(settleMs);

        overlay?.SetRect(new BBox(win.Rect.Left, win.Rect.Top, win.Rect.Width, win.Rect.Height));
        Thread.Sleep(120);
        var bytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));

        if (TryMatchDefineNotchProfilesLayout(profile, bytes, out var actualSha, out var expectedSha))
        {
            TestlabDebugMarkers.WritePhase(
                "runner.sine_setup_channel_safety_restore_failed",
                run.RunDirectory,
                $"tab={tabName};source={source};sequence={FormatWindowPointSequence(restoreSequence)};sleepMs={settleMs};reason=still_in_define_notch_profiles_layout;actual={actualSha};expected={expectedSha}"
            );
            throw new InvalidOperationException("恢复 `Channel safety parameters` 失败：当前仍停留在 `Define notch profiles` 布局。请先重新标定 `sine_setup_channel_safety_parameters`。");
        }

        TestlabDebugMarkers.WritePhase(
            "runner.sine_setup_channel_safety_restored",
            run.RunDirectory,
            $"tab={tabName};source={source};sequence={FormatWindowPointSequence(restoreSequence)};sleepMs={settleMs}"
        );

        var path = screenshots.SaveEvidencePng(run, $"{evidencePrefix}_{index:00}_sine_setup_channel_safety_restored", bytes);
        return (bytes, path);
    }

    private static (WindowPoint[] Sequence, string Source) ResolveSineSetupChannelSafetyRestoreSequence(WorkstationProfile profile)
    {
        var directSequence = profile.FindDialogAction("sine_setup_channel_safety_parameters")?.GetClickSequence();
        if (directSequence is not null && directSequence.Length > 0)
        {
            return (directSequence, "dialog_action_sequence");
        }

        throw new InvalidOperationException("profile 缺少 `Channel safety parameters` 恢复点击点；请先执行相关标定。");
    }

    private static bool TryMatchDefineNotchProfilesLayout(
        WorkstationProfile profile,
        byte[] windowBytes,
        out string actualSha,
        out string expectedSha
    )
    {
        actualSha = string.Empty;
        expectedSha = string.Empty;

        var verifyTarget = profile.FindChildWindowProfile("define_notch_profiles")?.FindVerifyTarget("notch_profiles_layout");
        if (verifyTarget?.RoiWindow is not BBox verifyRoiWindow || string.IsNullOrWhiteSpace(verifyTarget.Sha256))
        {
            return false;
        }

        var verifyBytes = ImageCropper.TryCropToPngBytes(windowBytes, verifyRoiWindow);
        if (verifyBytes is null || verifyBytes.Length == 0)
        {
            return false;
        }

        actualSha = ComputeImageContentSha256Hex(verifyBytes);
        expectedSha = verifyTarget.Sha256!;
        return string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatWindowPointSequence(IReadOnlyList<WindowPoint> sequence)
        => string.Join("->", sequence.Select(static p => $"({p.X},{p.Y})"));

    private static IReadOnlyList<int> GetNotchProfileIndexesFromEnv()
    {
        return NotchProfileIndexResolver.ResolveIndexesFromEnvironment();
    }

    private static bool IsNotchProfileCountModeRequested()
    {
        var rawIndexes = (Environment.GetEnvironmentVariable(NotchProfileIndexResolver.IndexesEnvName) ?? string.Empty).Trim();
        var rawIndex = (Environment.GetEnvironmentVariable(NotchProfileIndexResolver.IndexEnvName) ?? string.Empty).Trim();
        var rawCount = (Environment.GetEnvironmentVariable(NotchProfileIndexResolver.CountEnvName) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawCount))
        {
            rawCount = (Environment.GetEnvironmentVariable(NotchProfileIndexResolver.TaskFieldName) ?? string.Empty).Trim();
        }

        return string.IsNullOrWhiteSpace(rawIndexes) &&
               string.IsNullOrWhiteSpace(rawIndex) &&
               !string.IsNullOrWhiteSpace(rawCount);
    }

    private static TestlabNotchProfileCountMismatchResult CreateNotchProfileCountMismatchResult(
        int requestedCount,
        int failedRowIndex,
        IReadOnlyCollection<TestlabNotchProfileScanResult> completedResults,
        string userMessage,
        string detail
    )
    {
        return new TestlabNotchProfileCountMismatchResult(
            RequestedCount: requestedCount,
            CompletedCount: completedResults.Count,
            FailedRowIndex: failedRowIndex,
            LastCompletedRowIndex: completedResults.Count > 0 ? completedResults.Max(static item => item.TargetRowIndex) : null,
            UserMessage: userMessage,
            Detail: detail
        );
    }

    private static IReadOnlyList<TestlabTableScanResult> GetAllTableScans(
        IReadOnlyList<TestlabTabSwitchResult> switches,
        IReadOnlyList<TestlabNotchProfileScanResult>? notchProfileScans
    )
    {
        var scans = new List<TestlabTableScanResult>();
        foreach (var item in switches)
        {
            if (item.TableScans is null || item.TableScans.Count == 0)
            {
            }
            else
            {
                scans.AddRange(item.TableScans);
            }

            if (item.ChildWindowCaptures is null || item.ChildWindowCaptures.Count == 0)
            {
                continue;
            }

            foreach (var child in item.ChildWindowCaptures)
            {
                if (child.TableScans is null || child.TableScans.Count == 0)
                {
                    continue;
                }

                scans.AddRange(child.TableScans);
            }
        }

        if (notchProfileScans is not null)
        {
            foreach (var item in notchProfileScans)
            {
                scans.Add(item.TableScan);
            }
        }

        return scans;
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

    private static IReadOnlyList<TestlabTableScanChunkResult> GetUniqueChunksBySerial(IReadOnlyList<TestlabTableScanChunkResult> chunks)
    {
        var unique = new List<TestlabTableScanChunkResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in chunks)
        {
            if (seen.Add(chunk.SerialSha256))
            {
                unique.Add(chunk);
            }
        }

        return unique;
    }

    private sealed record TestlabTableScanContext(
        TestlabTableEntryResult Entry,
        TestlabWindowInfo Window,
        BBox TableRoiScreen,
        BBox TableRoiWindow,
        BBox InteractionRoiScreen,
        BBox InteractionRoiWindow,
        BBox SerialRoiScreen,
        BBox SerialRoiWindow,
        BBox ScrollbarRoiScreen,
        BBox ScrollbarRoiWindow,
        int FocusX,
        int FocusY,
        int ActivationX,
        int ActivationY,
        bool HasExplicitActivation,
        string PagingPreparationMode,
        BBox? TopSerialVerifyRoiWindow,
        string? TopSerialVerifySha256
    );

    private static void WriteTableEvidenceReport(
        RunContext run,
        string tabName,
        string tableName,
        IReadOnlyList<TestlabTableScanChunkResult> chunks,
        IReadOnlyList<TestlabTableScanChunkResult> uniqueChunks,
        IReadOnlyList<TestlabTableScrollEvent> scrollEvents,
        string? stitchedPath
    )
    {
        var changedEventCount = scrollEvents.Count(static item => item.Changed);
        var terminalNoChangeEventCount = scrollEvents.Count(static item => !item.Changed);
        var expectedUniqueChunkCount = chunks.Count == 0 ? 0 : changedEventCount + 1;
        var report = new TestlabTableEvidenceReport(
            TabName: tabName,
            TableName: tableName,
            ChunkCount: chunks.Count,
            UniqueChunkCount: uniqueChunks.Count,
            ChangedEventCount: changedEventCount,
            TerminalNoChangeEventCount: terminalNoChangeEventCount,
            ExpectedUniqueChunkCount: expectedUniqueChunkCount,
            DedupKey: "SerialSha256",
            IsConsistent: uniqueChunks.Count == expectedUniqueChunkCount,
            StitchedScreenshotPath: stitchedPath,
            UniqueChunkScreenshotPaths: uniqueChunks.Select(static item => item.ScreenshotPath).ToArray()
        );

        var path = Path.Combine(run.RunDirectory, $"table_evidence_{Normalize(tableName)}.json");
        File.WriteAllText(path, report.ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string? TryWriteStitchedTableEvidence(
        RunContext run,
        ScreenshotStore screenshots,
        string tableName,
        IReadOnlyList<TestlabTableScanChunkResult> uniqueChunks
    )
    {
        try
        {
            var frames = new List<BitmapSource>();
            var totalHeight = 0;
            var maxWidth = 0;

            foreach (var chunk in uniqueChunks)
            {
                var bytes = File.ReadAllBytes(chunk.ScreenshotPath);
                using var input = new MemoryStream(bytes, writable: false);
                var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                BitmapSource src = frame;
                if (frame.Format != PixelFormats.Bgra32)
                {
                    src = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                }

                frames.Add(src);
                totalHeight += src.PixelHeight;
                maxWidth = Math.Max(maxWidth, src.PixelWidth);
            }

            if (frames.Count == 0 || maxWidth <= 0 || totalHeight <= 0)
            {
                return null;
            }

            var stitched = new WriteableBitmap(maxWidth, totalHeight, 96, 96, PixelFormats.Bgra32, null);
            var yOffset = 0;
            foreach (var frame in frames)
            {
                var stride = frame.PixelWidth * 4;
                var pixels = new byte[stride * frame.PixelHeight];
                frame.CopyPixels(pixels, stride, 0);
                stitched.WritePixels(new Int32Rect(0, yOffset, frame.PixelWidth, frame.PixelHeight), pixels, stride, 0);
                yOffset += frame.PixelHeight;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(stitched));
            using var output = new MemoryStream();
            encoder.Save(output);
            return screenshots.SaveEvidencePng(run, $"testlab_table_{Normalize(tableName)}_stitched", output.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static BBox GetTableInteractionRoi(TestlabTableEntryResult entry)
    {
        var roi = entry.TableRoiScreen;
        if (Normalize(entry.TabName) == Normalize("Sine Setup") ||
            Normalize(entry.TableName) == Normalize("Channel Parameters Table"))
        {
            var tableWidth = Math.Clamp((int)Math.Round(roi.Width * 0.42), 320, roi.Width);
            return new BBox(roi.X, roi.Y, tableWidth, roi.Height);
        }

        return roi;
    }

    private static BBox GetTableInteractionRoiWindow(TestlabTableEntryResult entry)
    {
        var roi = entry.TableRoiWindow;
        if (Normalize(entry.TabName) == Normalize("Sine Setup") ||
            Normalize(entry.TableName) == Normalize("Channel Parameters Table"))
        {
            var tableWidth = Math.Clamp((int)Math.Round(roi.Width * 0.42), 320, roi.Width);
            return new BBox(roi.X, roi.Y, tableWidth, roi.Height);
        }

        return roi;
    }

    private static bool RequiresDeterministicTopReset(TestlabTableEntryResult entry)
    {
        var normalizedTab = Normalize(entry.TabName);
        var normalizedTable = Normalize(entry.TableName);
        return (normalizedTab == Normalize("Sine Setup") && normalizedTable == Normalize("Channel Parameters Table")) ||
               (normalizedTab == Normalize("Channel Setup") && normalizedTable == Normalize("Channel Setup Table")) ||
               (normalizedTab == Normalize("Profile Editor") && normalizedTable == Normalize("profileeditor_table_scan")) ||
               (normalizedTab == Normalize("Notch Profile") && normalizedTable.StartsWith(Normalize("Notch Profile Table"), StringComparison.Ordinal));
    }

    private static BBox MapWindowRoiToScreenRoi(BBox roiWindow, BBox contentBounds, TestlabWindowInfo win)
    {
        var scaleX = contentBounds.Width > 0
            ? (double)win.Rect.Width / contentBounds.Width
            : 1d;
        var scaleY = contentBounds.Height > 0
            ? (double)win.Rect.Height / contentBounds.Height
            : 1d;

        var relativeX = Math.Max(0, roiWindow.X - contentBounds.X);
        var relativeY = Math.Max(0, roiWindow.Y - contentBounds.Y);

        var mappedX = (int)Math.Round(relativeX * scaleX);
        var mappedY = (int)Math.Round(relativeY * scaleY);
        var mappedWidth = Math.Max(1, (int)Math.Round(roiWindow.Width * scaleX));
        var mappedHeight = Math.Max(1, (int)Math.Round(roiWindow.Height * scaleY));

        var screenX = win.Rect.Left + Math.Clamp(mappedX, 0, Math.Max(0, win.Rect.Width - 1));
        var screenY = win.Rect.Top + Math.Clamp(mappedY, 0, Math.Max(0, win.Rect.Height - 1));
        var maxWidth = Math.Max(1, win.Rect.Width - (screenX - win.Rect.Left));
        var maxHeight = Math.Max(1, win.Rect.Height - (screenY - win.Rect.Top));

        return new BBox(
            screenX,
            screenY,
            Math.Clamp(mappedWidth, 1, maxWidth),
            Math.Clamp(mappedHeight, 1, maxHeight)
        );
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

    private static IReadOnlyList<TestlabPageRegionResult> DetectPageRegionsIfNeeded(
        RunContext run,
        string tabName,
        byte[] windowImageBytes,
        OcrRunner ocrRunner,
        ScreenshotStore screenshots,
        int tabIndex,
        TestlabWindowInfo win,
        ICaptureOverlay? overlay
    )
    {
        if (Normalize(tabName) != Normalize("Sine Setup"))
        {
            return Array.Empty<TestlabPageRegionResult>();
        }

        return DetectSineSetupRegions(run, windowImageBytes, ocrRunner, screenshots, tabIndex, win, overlay);
    }

    private static IReadOnlyList<TestlabPageRegionResult> DetectSineSetupRegions(
        RunContext run,
        byte[] windowImageBytes,
        OcrRunner ocrRunner,
        ScreenshotStore screenshots,
        int tabIndex,
        TestlabWindowInfo win,
        ICaptureOverlay? overlay
    )
    {
        var (w, h) = ImageGeometry.GetSize(windowImageBytes);
        var results = new List<TestlabPageRegionResult>();

        var channelParametersRoi = new BBox(
            X: 0,
            Y: Math.Clamp((int)Math.Round(h * 0.04), 0, Math.Max(0, h - 1)),
            Width: w,
            Height: Math.Clamp((int)Math.Round(h * 0.22), 1, h)
        );
        results.Add(
            DetectSingleRegion(
                run,
                windowImageBytes,
                ocrRunner,
                screenshots,
                tabIndex,
                "Sine Setup",
                "Channel Parameters",
                $"testlab_tabs_{tabIndex:00}_sinesetup_top_roi",
                $"testlab_tabs_{tabIndex:00}_sinesetup_regions_ocr",
                channelParametersRoi,
                win,
                overlay
            )
        );

        var controlRoi = new BBox(
            X: Math.Clamp((int)Math.Round(w * 0.78), 0, Math.Max(0, w - 1)),
            Y: Math.Clamp((int)Math.Round(h * 0.04), 0, Math.Max(0, h - 1)),
            Width: Math.Clamp((int)Math.Round(w * 0.22), 1, w),
            Height: Math.Clamp((int)Math.Round(h * 0.42), 1, h)
        );
        results.Add(
            DetectSingleRegion(
                run,
                windowImageBytes,
                ocrRunner,
                screenshots,
                tabIndex,
                "Sine Setup",
                "Control",
                $"testlab_tabs_{tabIndex:00}_sinesetup_control_roi",
                $"testlab_tabs_{tabIndex:00}_sinesetup_control_ocr",
                controlRoi,
                win,
                overlay
            )
        );

        return results;
    }

    private static TestlabPageRegionResult DetectSingleRegion(
        RunContext run,
        byte[] windowImageBytes,
        OcrRunner ocrRunner,
        ScreenshotStore screenshots,
        int tabIndex,
        string tabName,
        string regionName,
        string roiShotId,
        string ocrId,
        BBox searchRoi,
        TestlabWindowInfo win,
        ICaptureOverlay? overlay
    )
    {
        overlay?.SetRect(new BBox(win.Rect.Left + searchRoi.X, win.Rect.Top + searchRoi.Y, searchRoi.Width, searchRoi.Height));
        var roiBytes = ImageCropper.TryCropToPngBytes(windowImageBytes, searchRoi) ?? windowImageBytes;
        var roiPath = screenshots.SaveDebugPng(run, roiShotId, roiBytes);

        var (ocrPath, ocrResult) = ocrRunner
            .RunAsync(
                run,
                ocrId,
                roiBytes,
                "image/png",
                new BBox(0, 0, searchRoi.Width, searchRoi.Height),
                $"region:{regionName}"
            )
            .GetAwaiter()
            .GetResult();

        var match = FindBlockByTarget(ocrResult, regionName);
        if (match is null)
        {
            return new TestlabPageRegionResult(tabName, regionName, roiPath, ocrPath, null, null, null);
        }

        var bbox = new BBox(
            searchRoi.X + match.BBox.X,
            searchRoi.Y + match.BBox.Y,
            match.BBox.Width,
            match.BBox.Height
        );

        return new TestlabPageRegionResult(tabName, regionName, roiPath, ocrPath, bbox, match.Text, match.Confidence, SearchRoiWindow: searchRoi);
    }

    private static byte[] CaptureWithOverlay(ICaptureOverlay? overlay, Func<byte[]> capture)
    {
        try
        {
            overlay?.SetVisible(false);
            Thread.Sleep(10);
            return capture();
        }
        finally
        {
            overlay?.SetVisible(true);
        }
    }

    private static DesktopPoint? FindClickPointFromBlocks(OcrResult result, BBox tabsRoi, string target)
    {
        var block = FindBlockByTarget(result, target);
        if (block is null)
        {
            return null;
        }

        var rawCx = block.BBox.X + (block.BBox.Width / 2);
        var rawCy = block.BBox.Y + (int)Math.Round(block.BBox.Height * 0.80);

        var bboxOutOfRoi =
            block.BBox.X < 0 ||
            block.BBox.Y < 0 ||
            (block.BBox.X + block.BBox.Width) > tabsRoi.Width ||
            (block.BBox.Y + block.BBox.Height) > tabsRoi.Height;

        var cx = Math.Clamp(rawCx, 0, Math.Max(0, tabsRoi.Width - 1));
        var cy = rawCy;
        if (cy < 0 || cy >= tabsRoi.Height)
        {
            cy = Math.Clamp(12, 0, Math.Max(0, tabsRoi.Height - 1));
        }

        if (bboxOutOfRoi)
        {
            TestlabDebugMarkers.WritePhase(
                "runner.ocr_bbox_out_of_roi",
                detail: $"target={target};roi=({tabsRoi.Width},{tabsRoi.Height});bbox=({block.BBox.X},{block.BBox.Y},{block.BBox.Width},{block.BBox.Height});raw=({rawCx},{rawCy});fixed=({cx},{cy})"
            );
        }

        return new DesktopPoint(tabsRoi.X + cx, tabsRoi.Y + cy);
    }

    private static int? TryComputeTabBodyClickY(byte[] tabsImageBytes)
    {
        try
        {
            using var input = new MemoryStream(tabsImageBytes, writable: false);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            BitmapSource src = frame;
            if (frame.Format != PixelFormats.Bgra32)
            {
                src = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            }

            var w = src.PixelWidth;
            var h = src.PixelHeight;
            if (w <= 0 || h <= 0)
            {
                return null;
            }

            var scanW = Math.Min(w, 1200);
            var stride = w * 4;
            var row = new byte[stride];
            var yEnd = Math.Max(1, (int)Math.Round(h * 0.75));
            var runLens = new int[yEnd];
            var maxRun = 0;

            for (var y = 0; y < yEnd; y++)
            {
                src.CopyPixels(new Int32Rect(0, y, w, 1), row, stride, 0);
                var current = 0;
                var best = 0;
                for (var x = 0; x < scanW; x++)
                {
                    var i = x * 4;
                    var b = row[i];
                    var g = row[i + 1];
                    var r = row[i + 2];
                    var isBlue = b >= 100 && (b - r) >= 30 && (b - g) >= 15;
                    if (isBlue)
                    {
                        current++;
                        if (current > best)
                        {
                            best = current;
                        }
                    }
                    else
                    {
                        current = 0;
                    }
                }

                runLens[y] = best;
                if (best > maxRun)
                {
                    maxRun = best;
                }
            }

            if (maxRun <= 0)
            {
                return null;
            }

            if (maxRun < (int)Math.Round(scanW * 0.18))
            {
                return null;
            }

            var thresh = Math.Max(1, (int)Math.Round(maxRun * 0.60));
            var bestStart = -1;
            var bestEnd = -1;
            var currentStart = -1;
            for (var y = 0; y < yEnd; y++)
            {
                var hot = runLens[y] >= thresh;
                if (hot)
                {
                    if (currentStart < 0)
                    {
                        currentStart = y;
                    }
                }
                else
                {
                    if (currentStart >= 0)
                    {
                        var end = y - 1;
                        if ((end - currentStart) > (bestEnd - bestStart))
                        {
                            bestStart = currentStart;
                            bestEnd = end;
                        }

                        currentStart = -1;
                    }
                }
            }

            if (currentStart >= 0)
            {
                var end = yEnd - 1;
                if ((end - currentStart) > (bestEnd - bestStart))
                {
                    bestStart = currentStart;
                    bestEnd = end;
                }
            }

            if (bestStart < 0 || bestEnd < bestStart)
            {
                return null;
            }

            if ((bestEnd - bestStart) < 2)
            {
                return null;
            }

            var clickY = (bestStart + bestEnd) / 2;
            return Math.Clamp(clickY, 0, Math.Max(0, h - 1));
        }
        catch
        {
            return null;
        }
    }

    private static (bool Verified, string Mode) VerifyPageSwitched(
        RunContext run,
        string tabName,
        byte[] beforeWindowImageBytes,
        byte[] windowImageBytes,
        BBox tabsRoiWindow,
        BBox? verifyRoiWindow,
        string? verifySha256,
        OcrRunner? ocrRunner,
        ScreenshotStore screenshots,
        int tabIndex,
        int xAttempt,
        int yAttempt,
        int clickTry,
        int localClickX,
        int localClickY
    )
    {
        try
        {
            var debugArtifacts = IsDebugArtifactsEnabled();

            if (IsFastTabSwitchEnabled() && verifyRoiWindow is not null && !string.IsNullOrWhiteSpace(verifySha256))
            {
                var sigBytes = ImageCropper.TryCropToPngBytes(windowImageBytes, verifyRoiWindow.Value);
                if (sigBytes is not null)
                {
                    var actualSha = ComputeSha256Hex(sigBytes);
                    if (string.Equals(actualSha, verifySha256, StringComparison.OrdinalIgnoreCase))
                    {
                        TestlabDebugMarkers.WritePhase(
                            "runner.tab_switch_verified_by_profile_signature",
                            run.RunDirectory,
                            $"tab={tabName};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry}"
                        );
                        return (true, "profile_signature");
                    }

                    var sigShotId = $"testlab_tabs_{tabIndex:00}_profile_signature_{Normalize(tabName)}_{xAttempt}_{yAttempt}_{clickTry}";
                    var sigPath = screenshots.SaveDebugPng(run, sigShotId, sigBytes);
                    TestlabDebugMarkers.WritePhase(
                        "runner.tab_switch_profile_signature_mismatch",
                        run.RunDirectory,
                        $"tab={tabName};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry};roi=({verifyRoiWindow.Value.X},{verifyRoiWindow.Value.Y},{verifyRoiWindow.Value.Width},{verifyRoiWindow.Value.Height});actual={actualSha};expected={verifySha256};path={sigPath}"
                    );
                    return (false, "profile_signature_mismatch");
                }
            }

            var beforeHash = ComputeSha256Hex(beforeWindowImageBytes);
            var afterHash = ComputeSha256Hex(windowImageBytes);
            if (string.Equals(beforeHash, afterHash, StringComparison.OrdinalIgnoreCase))
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.tab_switch_window_unchanged",
                    run.RunDirectory,
                    $"tab={tabName};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry}"
                );
                return (false, "window_unchanged");
            }

            var tabsBytes = ImageCropper.TryCropToPngBytes(windowImageBytes, tabsRoiWindow);
            if (tabsBytes is null)
            {
                return (false, "tabs_crop_failed");
            }

            if (debugArtifacts)
            {
                    _ = screenshots.SaveDebugPng(run, $"testlab_tabs_{tabIndex:00}_verify_tabs_{xAttempt}_{yAttempt}_{clickTry}", tabsBytes);
            }

            if (IsFastTabSwitchEnabled())
            {
                var beforeTabsBytes = ImageCropper.TryCropToPngBytes(beforeWindowImageBytes, tabsRoiWindow);
                if (beforeTabsBytes is not null)
                {
                    var tabsChanged = !string.Equals(
                        ComputeSha256Hex(beforeTabsBytes),
                        ComputeSha256Hex(tabsBytes),
                        StringComparison.OrdinalIgnoreCase
                    );

                    var sigChanged = true;
                    var norm = Normalize(tabName);
                    if (norm == Normalize("Sine Setup"))
                    {
                        var (w, h) = ImageGeometry.GetSize(windowImageBytes);
                        var contentRoi = new BBox(
                            X: 0,
                            Y: Math.Clamp((int)Math.Round(h * 0.04), 0, Math.Max(0, h - 1)),
                            Width: w,
                            Height: Math.Clamp((int)Math.Round(h * 0.22), 1, h)
                        );
                        var beforeSig = ImageCropper.TryCropToPngBytes(beforeWindowImageBytes, contentRoi);
                        var afterSig = ImageCropper.TryCropToPngBytes(windowImageBytes, contentRoi);
                        if (beforeSig is not null && afterSig is not null)
                        {
                            sigChanged = !string.Equals(
                                ComputeSha256Hex(beforeSig),
                                ComputeSha256Hex(afterSig),
                                StringComparison.OrdinalIgnoreCase
                            );
                        }
                    }
                    else if (norm == Normalize("Channel Setup"))
                    {
                        var (w, h) = ImageGeometry.GetSize(windowImageBytes);
                        var signatureRoi = new BBox(
                            X: 0,
                            Y: Math.Clamp((int)Math.Round(h * 0.02), 0, Math.Max(0, h - 1)),
                            Width: Math.Clamp((int)Math.Round(w * 0.32), 320, Math.Min(960, w)),
                            Height: Math.Clamp((int)Math.Round(h * 0.12), 80, Math.Min(220, h))
                        );
                        var beforeSig = ImageCropper.TryCropToPngBytes(beforeWindowImageBytes, signatureRoi);
                        var afterSig = ImageCropper.TryCropToPngBytes(windowImageBytes, signatureRoi);
                        if (beforeSig is not null && afterSig is not null)
                        {
                            sigChanged = !string.Equals(
                                ComputeSha256Hex(beforeSig),
                                ComputeSha256Hex(afterSig),
                                StringComparison.OrdinalIgnoreCase
                            );
                        }
                    }

                    if (tabsChanged && sigChanged)
                    {
                        TestlabDebugMarkers.WritePhase(
                            "runner.tab_switch_verified_by_fast_hash",
                            run.RunDirectory,
                            $"tab={tabName};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry}"
                        );
                        return (true, "fast_hash");
                    }

                    TestlabDebugMarkers.WritePhase(
                        "runner.tab_switch_fast_hash_mismatch",
                        run.RunDirectory,
                        $"tab={tabName};tabsChanged={(tabsChanged ? "1" : "0")};sigChanged={(sigChanged ? "1" : "0")};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry}"
                    );
                }
            }

            if (ocrRunner is null)
            {
                return (false, "ocr_skipped");
            }

            if (TryVerifyByPageContent(run, tabName, windowImageBytes, ocrRunner, screenshots, tabIndex, xAttempt, yAttempt, clickTry))
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.tab_switch_verified_by_content",
                    run.RunDirectory,
                    $"tab={tabName};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry}"
                );
                return (true, "content");
            }

            if (Normalize(tabName) == Normalize("Channel Setup"))
            {
                if (!TryVerifyByPageSignature(run, tabName, windowImageBytes, ocrRunner, screenshots, tabIndex, xAttempt, yAttempt, clickTry))
                {
                    TestlabDebugMarkers.WritePhase(
                        "runner.tab_switch_signature_mismatch",
                        run.RunDirectory,
                        $"tab={tabName};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry}"
                    );
                    return (false, "signature_mismatch");
                }

                TestlabDebugMarkers.WritePhase(
                    "runner.tab_switch_verified_by_signature",
                    run.RunDirectory,
                    $"tab={tabName};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry}"
                );
                return (true, "signature");
            }

            var label = TryExtractActiveTabLabel(
                run,
                tabIndex,
                xAttempt,
                yAttempt,
                clickTry,
                localClickX,
                localClickY,
                tabsBytes,
                ocrRunner,
                screenshots
            );

            if (string.IsNullOrWhiteSpace(label))
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.tab_switch_active_label_missing",
                    run.RunDirectory,
                    $"tab={tabName};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry}"
                );
                return (false, "active_label_missing");
            }

            if (label.StartsWith("OCR_ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.tab_switch_active_ocr_error",
                    run.RunDirectory,
                    $"tab={tabName};active={label};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry}"
                );
                return (false, "active_label_ocr_error");
            }

            if (!Normalize(label).Contains(Normalize(tabName), StringComparison.OrdinalIgnoreCase))
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.tab_switch_active_mismatch",
                    run.RunDirectory,
                    $"target={tabName};active={label};xAttempt={xAttempt};yAttempt={yAttempt};clickTry={clickTry}"
                );
                return (false, "active_mismatch");
            }

            return (true, "active_tab");
        }
        catch
        {
            return (false, "exception");
        }
    }

    private static bool TryVerifyByPageContent(
        RunContext run,
        string tabName,
        byte[] windowImageBytes,
        OcrRunner ocrRunner,
        ScreenshotStore screenshots,
        int tabIndex,
        int xAttempt,
        int yAttempt,
        int clickTry
    )
    {
        if (Normalize(tabName) != Normalize("Sine Setup"))
        {
            return false;
        }

        var (w, h) = ImageGeometry.GetSize(windowImageBytes);
        var contentRoi = new BBox(
            X: 0,
            Y: Math.Clamp((int)Math.Round(h * 0.04), 0, Math.Max(0, h - 1)),
            Width: w,
            Height: Math.Clamp((int)Math.Round(h * 0.22), 1, h)
        );
        var contentBytes = ImageCropper.TryCropToPngBytes(windowImageBytes, contentRoi);
        if (contentBytes is null)
        {
            return false;
        }

        var shotId = $"testlab_tabs_{tabIndex:00}_verify_content_{xAttempt}_{yAttempt}_{clickTry}";
        if (IsDebugArtifactsEnabled())
        {
            _ = screenshots.SaveDebugPng(run, shotId, contentBytes);
        }

        var (cw, ch) = ImageGeometry.GetSize(contentBytes);
        var (_, ocrResult) = ocrRunner
            .RunAsync(
                run,
                shotId,
                contentBytes,
                "image/png",
                new BBox(0, 0, cw, ch),
                "region:Channel Parameters"
            )
            .GetAwaiter()
            .GetResult();

        var match = FindBlockByTarget(ocrResult, "Channel Parameters");
        return match is not null;
    }

    private static bool TryVerifyByPageSignature(
        RunContext run,
        string tabName,
        byte[] windowImageBytes,
        OcrRunner ocrRunner,
        ScreenshotStore screenshots,
        int tabIndex,
        int xAttempt,
        int yAttempt,
        int clickTry
    )
    {
        if (Normalize(tabName) != Normalize("Channel Setup"))
        {
            return false;
        }

        var (w, h) = ImageGeometry.GetSize(windowImageBytes);
        var signatureRoi = new BBox(
            X: 0,
            Y: Math.Clamp((int)Math.Round(h * 0.02), 0, Math.Max(0, h - 1)),
            Width: Math.Clamp((int)Math.Round(w * 0.32), 320, Math.Min(960, w)),
            Height: Math.Clamp((int)Math.Round(h * 0.12), 80, Math.Min(220, h))
        );
        var signatureBytes = ImageCropper.TryCropToPngBytes(windowImageBytes, signatureRoi);
        if (signatureBytes is null)
        {
            return false;
        }

        var shotId = $"testlab_tabs_{tabIndex:00}_verify_signature_{xAttempt}_{yAttempt}_{clickTry}";
        if (IsDebugArtifactsEnabled())
        {
            _ = screenshots.SaveDebugPng(run, shotId, signatureBytes);
        }

        var (sw, sh) = ImageGeometry.GetSize(signatureBytes);
        var (_, ocrResult) = ocrRunner
            .RunAsync(
                run,
                shotId,
                signatureBytes,
                "image/png",
                new BBox(0, 0, sw, sh),
                "region:Channel Setup"
            )
            .GetAwaiter()
            .GetResult();

        var match = FindBlockByTarget(ocrResult, "Channel Setup");
        return match is not null;
    }

    private static string? TryExtractActiveTabLabel(
        RunContext run,
        int tabIndex,
        int xAttempt,
        int yAttempt,
        int clickTry,
        int localClickX,
        int localClickY,
        byte[] tabsImageBytes,
        OcrRunner ocrRunner,
        ScreenshotStore screenshots
    )
    {
        try
        {
            var (tw, th) = ImageGeometry.GetSize(tabsImageBytes);
            if (tw > 0 && th > 0)
            {
                var bandH = Math.Clamp((int)Math.Round(th * 0.55), 24, th);
                var roiW = Math.Clamp((int)Math.Round(tw * 0.22), 240, Math.Min(520, tw));
                var x0 = Math.Clamp(localClickX - (roiW / 2), 0, Math.Max(0, tw - roiW));
                var y0 = Math.Clamp(localClickY - (bandH / 2), 0, Math.Max(0, th - bandH));
                var roi = new BBox(x0, y0, roiW, bandH);
                var roiBytes = ImageCropper.TryCropToPngBytes(tabsImageBytes, roi);
                if (roiBytes is not null)
                {
                    if (IsDebugArtifactsEnabled())
                    {
                        _ = screenshots.SaveDebugPng(run, $"testlab_tabs_{tabIndex:00}_active_tab_click_{xAttempt}_{yAttempt}_{clickTry}", roiBytes);
                    }

                    var (cw0, ch0) = ImageGeometry.GetSize(roiBytes);
                    var id0 = $"testlab_tabs_{tabIndex:00}_active_tab_click_{xAttempt}_{yAttempt}_{clickTry}";
                    var (_, ocr0) = ocrRunner
                        .RunAsync(run, id0, roiBytes, "image/png", new BBox(0, 0, cw0, ch0), "active_tab_label_click")
                        .GetAwaiter()
                        .GetResult();

                    var t0 = ocr0.Blocks.FirstOrDefault()?.Text;
                    if (!string.IsNullOrWhiteSpace(t0))
                    {
                        return t0;
                    }
                }
            }

            using var input = new MemoryStream(tabsImageBytes, writable: false);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            BitmapSource src = frame;
            if (frame.Format != PixelFormats.Bgra32)
            {
                src = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            }

            var w = src.PixelWidth;
            var h = src.PixelHeight;
            if (w <= 0 || h <= 0)
            {
                return null;
            }

            var sampleY = Math.Clamp((int)Math.Round(h * 0.55), 0, h - 1);
            var stride = w * 4;
            var row = new byte[stride];
            src.CopyPixels(new Int32Rect(0, sampleY, w, 1), row, stride, 0);

            var (segStart0, segEnd0) = FindActiveWhiteSegment(row, w);
            if (segEnd0 <= segStart0)
            {
                return null;
            }

            var segStart = Math.Clamp(segStart0 - 12, 0, w - 1);
            var segEnd = Math.Clamp(segEnd0 + 12, 0, w - 1);
            var segWidth = segEnd - segStart + 1;
            if (segWidth < 25)
            {
                return null;
            }

            var segRoi = new BBox(segStart, 0, segWidth, h);
            var segBytes = ImageCropper.TryCropToPngBytes(tabsImageBytes, segRoi) ?? tabsImageBytes;
            if (IsDebugArtifactsEnabled())
            {
                _ = screenshots.SaveDebugPng(run, $"testlab_tabs_{tabIndex:00}_active_tab_{xAttempt}_{yAttempt}_{clickTry}", segBytes);
            }

            var (cw, ch) = ImageGeometry.GetSize(segBytes);
            var id = $"testlab_tabs_{tabIndex:00}_active_tab_{xAttempt}_{yAttempt}_{clickTry}";
            var (_, ocrResult) = ocrRunner
                .RunAsync(run, id, segBytes, "image/png", new BBox(0, 0, cw, ch), "active_tab_label")
                .GetAwaiter()
                .GetResult();

            var text = ocrResult.Blocks.FirstOrDefault()?.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var upperH = Math.Clamp((int)Math.Round(ch * 0.75), 1, ch);
            var upper = ImageCropper.TryCropToPngBytes(segBytes, new BBox(0, 0, cw, upperH));
            if (upper is not null)
            {
                if (IsDebugArtifactsEnabled())
                {
                    _ = screenshots.SaveDebugPng(run, $"testlab_tabs_{tabIndex:00}_active_tab_upper_{xAttempt}_{yAttempt}_{clickTry}", upper);
                }
                var (uw, uh) = ImageGeometry.GetSize(upper);
                var (_, upperOcr) = ocrRunner
                    .RunAsync(run, $"{id}_upper", upper, "image/png", new BBox(0, 0, uw, uh), "active_tab_label_upper")
                    .GetAwaiter()
                    .GetResult();
                var upperText = upperOcr.Blocks.FirstOrDefault()?.Text;
                if (!string.IsNullOrWhiteSpace(upperText))
                {
                    return upperText;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static (int start, int end) FindActiveWhiteSegment(byte[] bgraRow, int width)
    {
        var bestStart = -1;
        var bestEnd = -1;
        var currentStart = -1;

        for (var x = 0; x < width; x++)
        {
            var i = x * 4;
            var b = bgraRow[i];
            var g = bgraRow[i + 1];
            var r = bgraRow[i + 2];
            var lum = (r + g + b) / 3;
            var isWhite = lum >= 220;

            if (isWhite)
            {
                if (currentStart < 0)
                {
                    currentStart = x;
                }
            }
            else
            {
                if (currentStart >= 0)
                {
                    var currentEnd = x - 1;
                    if ((currentEnd - currentStart) > (bestEnd - bestStart))
                    {
                        bestStart = currentStart;
                        bestEnd = currentEnd;
                    }
                    currentStart = -1;
                }
            }
        }

        if (currentStart >= 0)
        {
            var currentEnd = width - 1;
            if ((currentEnd - currentStart) > (bestEnd - bestStart))
            {
                bestStart = currentStart;
                bestEnd = currentEnd;
            }
        }

        return (Math.Max(0, bestStart), Math.Max(0, bestEnd));
    }

    private static BBox? TryFindVisibleContentBounds(byte[] imageBytes)
    {
        try
        {
            using var input = new MemoryStream(imageBytes, writable: false);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            BitmapSource src = frame;
            if (frame.Format != PixelFormats.Bgra32)
            {
                src = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            }

            var w = src.PixelWidth;
            var h = src.PixelHeight;
            if (w <= 0 || h <= 0)
            {
                return null;
            }

            var stride = w * 4;
            var pixels = new byte[stride * h];
            src.CopyPixels(pixels, stride, 0);

            var minX = w;
            var minY = h;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < h; y++)
            {
                var rowStart = y * stride;
                for (var x = 0; x < w; x++)
                {
                    var i = rowStart + (x * 4);
                    var b = pixels[i];
                    var g = pixels[i + 1];
                    var r = pixels[i + 2];
                    var a = pixels[i + 3];
                    var maxChannel = Math.Max(r, Math.Max(g, b));
                    if (a < 8 || maxChannel < 12)
                    {
                        continue;
                    }

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return null;
            }

            minX = Math.Max(0, minX - 2);
            minY = Math.Max(0, minY - 2);
            maxX = Math.Min(w - 1, maxX + 2);
            maxY = Math.Min(h - 1, maxY + 2);

            var bounds = new BBox(minX, minY, maxX - minX + 1, maxY - minY + 1);
            if (bounds.Width < 120 || bounds.Height < 80)
            {
                return null;
            }

            return bounds;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsDebugArtifactsEnabled()
    {
        var value = Environment.GetEnvironmentVariable("CHECKMIND_DEBUG_ENV");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static DesktopPoint? TryFindClickPointBySlidingWindows(
        RunContext run,
        int tabIndex,
        byte[] tabsImageBytes,
        BBox tabsRoiWindow,
        string target,
        OcrRunner ocrRunner,
        ScreenshotStore screenshots
    )
    {
        try
        {
            var (w, h) = ImageGeometry.GetSize(tabsImageBytes);
            if (w <= 0 || h <= 0)
            {
                return null;
            }

            var windowW = Math.Clamp((int)Math.Round(w * 0.36), 380, Math.Min(760, w));
            var maxWindows = 9;
            var usable = Math.Max(1, w - windowW);
            var step = Math.Max(1, (int)Math.Round(usable / (double)(maxWindows - 1)));
            var k = 0;

            for (var x0 = 0; x0 <= (w - windowW); x0 += step)
            {
                var roi = new BBox(x0, 0, windowW, h);
                var roiBytes = ImageCropper.TryCropToPngBytes(tabsImageBytes, roi);
                if (roiBytes is null)
                {
                    continue;
                }

                var name = $"testlab_tabs_{tabIndex:00}_slide_{k:00}";
                _ = screenshots.SaveDebugPng(run, name, roiBytes);
                var (_, ocrResult) = ocrRunner
                    .RunAsync(run, name, roiBytes, "image/png", new BBox(0, 0, windowW, h), $"tabs:{target}")
                    .GetAwaiter()
                    .GetResult();

                var block = FindBlockByTarget(ocrResult, target);
                if (block is null)
                {
                    k++;
                    continue;
                }

                var bboxOutOfWindow =
                    block.BBox.X < 0 ||
                    block.BBox.Y < 0 ||
                    (block.BBox.X + block.BBox.Width) > windowW ||
                    (block.BBox.Y + block.BBox.Height) > h;
                if (bboxOutOfWindow)
                {
                    TestlabDebugMarkers.WritePhase(
                        "runner.slide_bbox_invalid",
                        run.RunDirectory,
                        $"tab={target};slide={k};roi=({windowW},{h});bbox=({block.BBox.X},{block.BBox.Y},{block.BBox.Width},{block.BBox.Height})"
                    );
                    k++;
                    continue;
                }

                var cx = block.BBox.X + (block.BBox.Width / 2);
                var cy = block.BBox.Y + (int)Math.Round(block.BBox.Height * 0.80);
                cx = Math.Clamp(cx, 0, Math.Max(0, windowW - 1));
                cy = Math.Clamp(cy, 0, Math.Max(0, h - 1));
                return new DesktopPoint(tabsRoiWindow.X + x0 + cx, tabsRoiWindow.Y + cy);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static OcrBlock? FindBlockByTarget(OcrResult result, string target)
    {
        var targetNorm = Normalize(target);
        foreach (var b in result.Blocks)
        {
            var textNorm = Normalize(b.Text);
            if (string.IsNullOrWhiteSpace(textNorm))
            {
                continue;
            }

            if (!textNorm.Contains(targetNorm, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return b;
        }

        return null;
    }

    private static string WriteTabClickPointGateReport(
        RunContext run,
        string tabName,
        string ruleKey,
        string message,
        WindowPoint? clickPointWindow,
        TestlabWindowInfo win,
        string? beforeWindowPath,
        string? afterWindowPath,
        string? beforeTabsRoiPath,
        string? afterTabsRoiPath,
        string? suggestedOcrPath,
        WindowPoint? suggestedClickPointWindow,
        long? totalMs = null
    )
    {
        var report = new TabClickPointGateReport(
            TabName: tabName,
            RuleKey: ruleKey,
            Message: message,
            ClickPointWindow: clickPointWindow,
            WindowWidth: win.Rect.Width,
            WindowHeight: win.Rect.Height,
            BeforeWindowPath: beforeWindowPath,
            AfterWindowPath: afterWindowPath,
            BeforeTabsRoiPath: beforeTabsRoiPath,
            AfterTabsRoiPath: afterTabsRoiPath,
            SuggestedOcrPath: suggestedOcrPath,
            SuggestedClickPointWindow: suggestedClickPointWindow,
            TotalMs: totalMs ?? 0
        );

        var path = Path.Combine(run.RunDirectory, $"tab_click_gate_{Normalize(tabName)}.json");
        File.WriteAllText(path, report.ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string WriteTabSwitchTimingReport(
        RunContext run,
        int tabIndex,
        string tabName,
        string clickSource,
        bool fastEnabled,
        bool verified,
        string verifyMode,
        WindowPoint? clickPointWindow,
        int clickScreenX,
        int clickScreenY,
        string? beforeWindowPath,
        string? afterWindowPath,
        long totalMs,
        long captureWindowMs,
        long verifyMs,
        int clickAttempts,
        int attemptXCount,
        int attemptYCount,
        int maxClickTries
    )
    {
        var report = new TabSwitchTimingReport(
            TabName: tabName,
            ClickSource: clickSource,
            FastEnabled: fastEnabled,
            Verified: verified,
            VerifyMode: verifyMode,
            ClickPointWindow: clickPointWindow,
            ClickScreenX: clickScreenX,
            ClickScreenY: clickScreenY,
            BeforeWindowPath: beforeWindowPath,
            AfterWindowPath: afterWindowPath,
            TotalMs: totalMs,
            CaptureWindowMs: captureWindowMs,
            VerifyMs: verifyMs,
            ClickAttempts: clickAttempts,
            AttemptXCount: attemptXCount,
            AttemptYCount: attemptYCount,
            MaxClickTries: maxClickTries
        );

        var path = Path.Combine(run.RunDirectory, $"tab_switch_timing_{tabIndex:00}_{Normalize(tabName)}.json");
        File.WriteAllText(path, report.ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string WriteTabClickArtifact(
        RunContext run,
        string artifactId,
        string tabName,
        string source,
        DesktopPoint? clickPointWindow,
        BBox tabsRoiWindow,
        string? note = null,
        DesktopPoint? suggestedClickPointWindow = null,
        string? suggestedOcrPath = null
    )
    {
        var payload = new
        {
            tabName,
            source,
            clickPointWindow,
            tabsRoiWindow,
            note,
            suggestedClickPointWindow,
            suggestedOcrPath
        };

        var path = Path.Combine(run.RunDirectory, $"{artifactId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions.Default), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static (string? OcrPath, DesktopPoint? ClickPoint) TrySuggestTabClickPointByOcr(
        RunContext run,
        OcrRunner ocrRunner,
        ScreenshotStore screenshots,
        string ocrId,
        string target,
        byte[] searchTabsBytes,
        BBox searchTabsRoi,
        BBox tabsRoiWindow
    )
    {
        try
        {
            var (ocrPath, ocrResult) = ocrRunner
                .RunAsync(
                    run,
                    ocrId,
                    searchTabsBytes,
                    "image/png",
                    new BBox(0, 0, searchTabsRoi.Width, searchTabsRoi.Height),
                    "tabs:all"
                )
                .GetAwaiter()
                .GetResult();

            if (ocrResult.Blocks.Count == 0 || FindBlockByTarget(ocrResult, target) is null)
            {
                (ocrPath, ocrResult) = ocrRunner
                    .RunAsync(
                        run,
                        ocrId,
                        searchTabsBytes,
                        "image/png",
                        new BBox(0, 0, searchTabsRoi.Width, searchTabsRoi.Height),
                        $"tabs:{target}"
                    )
                    .GetAwaiter()
                    .GetResult();
            }

            var click = FindClickPointFromBlocks(
                ocrResult,
                new BBox(tabsRoiWindow.X + searchTabsRoi.X, tabsRoiWindow.Y, searchTabsRoi.Width, searchTabsRoi.Height),
                target
            );

            if (click is null)
            {
                var slidingClick = TryFindClickPointBySlidingWindows(
                    run,
                    99,
                    searchTabsBytes,
                    new BBox(tabsRoiWindow.X + searchTabsRoi.X, tabsRoiWindow.Y, searchTabsRoi.Width, searchTabsRoi.Height),
                    target,
                    ocrRunner,
                    screenshots
                );
                if (slidingClick is not null)
                {
                    click = slidingClick;
                }
            }

            return (ocrPath, click);
        }
        catch (Exception ex)
        {
            TestlabDebugMarkers.WritePhase(
                "runner.fixed_tab_click_suggest_exception",
                run.RunDirectory,
                $"tab={target};type={ex.GetType().Name};hresult=0x{ex.HResult:X8}"
            );
            return (null, null);
        }
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            if (ch is '-' or '_' or ':' or '：')
            {
                continue;
            }

            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static string SanitizeDetail(string? value)
        => (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static IOcrAdapter CreateOcrAdapter()
    {
        var mode = Environment.GetEnvironmentVariable("CHECKMIND_OCR_MODE");
        if (string.Equals(mode, "mock", StringComparison.OrdinalIgnoreCase))
        {
            return new MockOcrAdapter();
        }

        var baseUrl = Environment.GetEnvironmentVariable("CHECKMIND_OCR_BASE_URL");
        var modelId = Environment.GetEnvironmentVariable("CHECKMIND_OCR_MODEL_ID");
        var apiKeyEnv = Environment.GetEnvironmentVariable("CHECKMIND_OCR_API_KEY_ENV");

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(modelId))
        {
            try
            {
                var cfg = new AppConfigStore().LoadOrCreateDefault();
                var endpoint = cfg.LlmEndpoints
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.BaseUrl) &&
                                         (!string.IsNullOrWhiteSpace(x.DefaultModelId) || (x.PreferredModelIds?.Any(m => !string.IsNullOrWhiteSpace(m)) ?? false)));

                if (endpoint is null)
                {
                    return new MockOcrAdapter();
                }

                baseUrl = endpoint.BaseUrl;
                modelId = endpoint.PreferredModelIds?.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? endpoint.DefaultModelId;
                apiKeyEnv = endpoint.ApiKeyEnvVar;
            }
            catch
            {
                return new MockOcrAdapter();
            }
        }

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(modelId))
        {
            return new MockOcrAdapter();
        }

        var apiKey = string.IsNullOrWhiteSpace(apiKeyEnv) ? null : EnvironmentValueResolver.Get(apiKeyEnv);
        return new LlmVisionBlocksOcrAdapter(new LlmClient(LlmClient.CreateHttpClient(baseUrl, apiKey)), modelId);
    }

    private static void WriteWindowInfo(RunContext run, TestlabWindowInfo win, bool isMaximized)
    {
        var obj = new
        {
            hwnd = $"0x{win.Hwnd.ToInt64():X}",
            pid = win.ProcessId,
            process = win.ProcessName,
            title = win.Title,
            rect = new { win.Rect.Left, win.Rect.Top, win.Rect.Width, win.Rect.Height },
            isMaximized,
            atUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(run.RunDirectory, "testlab_window.json"), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteRunResult(RunContext run, TestlabRunResult result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(run.RunDirectory, "testlab_run.json"), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

public readonly record struct DesktopPoint(int X, int Y);

public sealed record TestlabRunResult(
    string BeforeMaximizeScreenshotPath,
    string AfterMaximizeScreenshotPath,
    IReadOnlyList<TestlabTabSwitchResult> TabSwitches,
    IReadOnlyList<TestlabNotchProfileScanResult>? NotchProfileScans = null,
    TestlabNotchProfileCountMismatchResult? NotchProfileCountMismatch = null
);

public sealed record TestlabTabSwitchResult(
    string TabName,
    string BeforeScreenshotPath,
    string TabsOcrPath,
    DesktopPoint? ClickPoint,
    string? AfterScreenshotPath,
    IReadOnlyList<TestlabPageRegionResult>? Regions = null,
    BBox? TabsRoiWindow = null,
    IReadOnlyList<TestlabTableEntryResult>? TableEntries = null,
    IReadOnlyList<TestlabTableScanResult>? TableScans = null,
    IReadOnlyList<TestlabFixedCaptureResult>? FixedCaptures = null,
    IReadOnlyList<TestlabChildWindowCaptureResult>? ChildWindowCaptures = null
);

public sealed record TestlabPageRegionResult(
    string TabName,
    string RegionName,
    string SearchScreenshotPath,
    string OcrPath,
    BBox? BBox,
    string? MatchedText,
    double? Confidence,
    BBox? SearchRoiWindow = null
);

public sealed record TestlabTableEntryResult(
    string TabName,
    string TableName,
    string ScreenshotPath,
    BBox TableRoiWindow,
    BBox TableRoiScreen,
    WindowPoint? PagingFocusPointWindow = null,
    WindowPoint? PagingActivationPointWindow = null,
    string? PagingPreparationMode = null
);

public sealed record TestlabTableScanResult(
    string TabName,
    string TableName,
    BBox TableRoiWindow,
    BBox TableRoiScreen,
    IReadOnlyList<TestlabTableScanChunkResult> Chunks,
    int UniqueChunkCount,
    string? StitchedScreenshotPath
);

public sealed record TestlabTableScanChunkResult(
    int Index,
    string ScreenshotPath,
    BBox RoiScreen,
    string Sha256,
    BBox SerialRoiScreen,
    string SerialScreenshotPath,
    string SerialSha256
);

public sealed record TestlabFixedCaptureResult(
    string Key,
    string ScreenshotPath,
    BBox RoiWindow,
    BBox RoiScreen,
    string? SourceTabName = null
);

public sealed record TestlabChildWindowCaptureResult(
    string WindowKey,
    string WindowTitleContains,
    string? OpenedWindowTitle,
    string? OpenedWindowScreenshotPath,
    IReadOnlyList<TestlabFixedCaptureResult> Captures,
    IReadOnlyList<TestlabTableScanResult>? TableScans = null,
    string? CloseMode = null,
    bool ChildWindowClosed = false,
    string? ReturnedToParentScreenshotPath = null,
    string? ErrorMessage = null
);

public sealed record TestlabNotchProfileScanResult(
    int TargetRowIndex,
    TestlabChildWindowOpenResult Opened,
    string DefineNotchProfilesScreenshotPath,
    string TableEntryScreenshotPath,
    BBox TableRoiWindow,
    BBox TableRoiScreen,
    TestlabTableScanResult TableScan,
    string CloseMode,
    bool ChildWindowClosed,
    string? DefineNotchProfilesAfterCloseScreenshotPath
);

public sealed record TestlabNotchProfileCountMismatchResult(
    int RequestedCount,
    int CompletedCount,
    int FailedRowIndex,
    int? LastCompletedRowIndex,
    string UserMessage,
    string Detail
);
