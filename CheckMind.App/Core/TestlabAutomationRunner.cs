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

            var result = new TestlabRunResult(beforePath, afterPath, switches);
            WriteCoverage(run, GetAllTableScans(switches));
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
                        var normalizedTarget = Normalize(target);
                        var postClickProbeSleepMs = isFastFixedClick
                            ? (normalizedTarget == Normalize("Sine Setup") ? 80 : 40)
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
                            ? (normalizedTarget == Normalize("Sine Setup") ? 220 : 120)
                            : 580;
                        Thread.Sleep(preCaptureSleepMs);
                        clickAttempts++;
                        var capSw = Stopwatch.StartNew();
                        shot1Bytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
                        capSw.Stop();
                        captureWindowMs += capSw.ElapsedMilliseconds;

                        if (isFastFixedClick &&
                            normalizedTarget == Normalize("Sine Setup") &&
                            !string.IsNullOrWhiteSpace(verifySha256))
                        {
                            Thread.Sleep(90);
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
                        var outcome = VerifyPageSwitched(
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
                        verSw.Stop();
                        verifyMs += verSw.ElapsedMilliseconds;
                        verified = outcome.Verified;
                        verifyMode = outcome.Mode;

                        if (!verified && clickSource == "fixed" && fastSwitch &&
                            string.Equals(verifyMode, "profile_signature_mismatch", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(verifySha256))
                        {
                            var retryCount = Math.Clamp(GetIntEnv("CHECKMIND_PROFILE_SIGNATURE_VERIFY_RETRY", 2), 0, 6);
                            var retrySleepMs = Math.Clamp(GetIntEnv("CHECKMIND_PROFILE_SIGNATURE_VERIFY_RETRY_SLEEP_MS", 60), 0, 500);
                            for (var verifyRetry = 0; verifyRetry < retryCount; verifyRetry++)
                            {
                                Thread.Sleep(retrySleepMs);
                                capSw = Stopwatch.StartNew();
                                shot1Bytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
                                capSw.Stop();
                                captureWindowMs += capSw.ElapsedMilliseconds;

                                verSw = Stopwatch.StartNew();
                                outcome = VerifyPageSwitched(
                                    run,
                                    target,
                                    shot0Bytes,
                                    shot1Bytes,
                                    tabsRoiWindow,
                                    verifyRoiWindow,
                                    verifySha256,
                                    null,
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

            var tableEntries = CaptureTableEntriesIfNeeded(run, target, shot1Bytes, capturer, screenshots, i, win, tabsRoiWindow, overlay);
            var tableScans = ScanTablesVerticallyIfNeeded(run, target, controller, capturer, screenshots, win, tableEntries, overlay);
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
                    TableScans: tableScans
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

        var entryProfileRoi = pageProfile?.FindCaptureTarget("table_entry")?.RoiWindow;
        var scanProfileRoi = pageProfile?.FindCaptureTarget("table_scan")?.RoiWindow ?? pageProfile?.CaptureRoiWindow;
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
            new TestlabTableEntryResult(tabName, tableName, tablePath, scanRoiWindow, scanRoiScreen)
        };
    }

    private static IReadOnlyList<TestlabTableScanResult>? ScanTablesVerticallyIfNeeded(
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
        var normalizedTab = Normalize(tabName);
        if (normalizedTab != Normalize("Sine Setup") && normalizedTab != Normalize("Channel Setup"))
        {
            return null;
        }

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
            var roi = entry.TableRoiScreen;
            var interactionRoi = GetTableInteractionRoi(entry);
            var roiWindow = entry.TableRoiWindow;
            var interactionRoiWindow = GetTableInteractionRoiWindow(entry);
            var focusX = interactionRoi.X + Math.Clamp((int)Math.Round(interactionRoi.Width * 0.35), 60, Math.Max(60, interactionRoi.Width - 24));
            var focusY = interactionRoi.Y + (interactionRoi.Height / 2);
            var scrollbarRoi = new BBox(
                interactionRoi.X + Math.Max(0, interactionRoi.Width - 18),
                interactionRoi.Y,
                Math.Min(18, interactionRoi.Width),
                interactionRoi.Height
            );
            var scrollbarRoiWindow = new BBox(
                interactionRoiWindow.X + Math.Max(0, interactionRoiWindow.Width - 18),
                interactionRoiWindow.Y,
                Math.Min(18, interactionRoiWindow.Width),
                interactionRoiWindow.Height
            );
            TestlabDebugMarkers.WritePhase(
                "runner.table_scroll_target",
                run.RunDirectory,
                $"table={entry.TableName};roi=({roi.X},{roi.Y},{roi.Width},{roi.Height});roiWindow=({roiWindow.X},{roiWindow.Y},{roiWindow.Width},{roiWindow.Height});interaction=({interactionRoi.X},{interactionRoi.Y},{interactionRoi.Width},{interactionRoi.Height});interactionWindow=({interactionRoiWindow.X},{interactionRoiWindow.Y},{interactionRoiWindow.Width},{interactionRoiWindow.Height});focus=({focusX},{focusY});scrollbar=({scrollbarRoi.X},{scrollbarRoi.Y},{scrollbarRoi.Width},{scrollbarRoi.Height})"
            );
            controller.ClickScreenPoint(focusX, focusY);
            Thread.Sleep(80);
            controller.ClickScreenPoint(focusX, focusY);
            Thread.Sleep(80);
            var topSerialVerifyTarget = fixedProfile?
                .FindPageProfile(entry.TabName)?
                .FindVerifyTarget("top_serial");
            var topSerialVerifyRoiWindow = topSerialVerifyTarget?.RoiWindow;
            var topSerialVerifySha256 = topSerialVerifyTarget?.Sha256
                ?? fixedProfile?.FindPageProfile(entry.TabName)?.TopSerialVerifySha256;

            var resetTopStable = ResetTableToTopBeforeScan(
                run,
                controller,
                overlay,
                capturer,
                screenshots,
                entry,
                win,
                roi,
                roiWindow,
                serialX: 0,
                serialWidthHint: Math.Clamp((int)Math.Round(roi.Width * 0.12), 80, 280),
                scrollbarRoi,
                scrollbarRoiWindow,
                focusX,
                focusY,
                pauseMs,
                topSerialVerifyRoiWindow,
                topSerialVerifySha256
            );
            if (!resetTopStable)
            {
                TestlabDebugMarkers.WritePhase(
                    "runner.table_scan_blocked_unstable_top",
                    run.RunDirectory,
                    $"table={entry.TableName};tab={entry.TabName};reason=reset_top_not_stable"
                );
                results.Add(new TestlabTableScanResult(entry.TabName, entry.TableName, entry.TableRoiWindow, entry.TableRoiScreen, Array.Empty<TestlabTableScanChunkResult>(), 0, null));
                continue;
            }

            var serialXRaw = Environment.GetEnvironmentVariable("CHECKMIND_TABLE_SCAN_SERIAL_X");
            var serialX = int.TryParse(serialXRaw, out var sx) ? sx : 0;
            serialX = Math.Clamp(serialX, 0, Math.Max(0, roi.Width - 1));

            var serialWidthRaw = Environment.GetEnvironmentVariable("CHECKMIND_TABLE_SCAN_SERIAL_WIDTH");
            var serialWidth = int.TryParse(serialWidthRaw, out var sw)
                ? sw
                : Math.Clamp((int)Math.Round(roi.Width * 0.12), 80, 280);
            serialWidth = Math.Clamp(serialWidth, 20, Math.Max(20, roi.Width - serialX));

            var serialRoi = new BBox(roi.X + serialX, roi.Y, serialWidth, roi.Height);
            var serialRoiWindow = new BBox(roiWindow.X + serialX, roiWindow.Y, serialWidth, roiWindow.Height);

            var chunks = new List<TestlabTableScanChunkResult>();
            string? lastStateHash = null;
            var scrollEvents = new List<TestlabTableScrollEvent>();

            (TestlabTableScanChunkResult Chunk, string ScrollbarHash) CaptureChunk(int chunkIndex)
            {
                controller.ClickScreenPoint(focusX, focusY);
                Thread.Sleep(60);
                overlay?.SetRect(roi);
                Thread.Sleep(80);
                var windowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
                _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(entry.TableName)}_window_v_{chunkIndex:00}", windowBytes);
                var bytes = ImageCropper.TryCropToPngBytes(windowBytes, roiWindow) ?? windowBytes;
                var path = screenshots.SaveEvidencePng(run, $"testlab_table_{Normalize(entry.TableName)}_v_{chunkIndex:00}", bytes);
                var frameHash = ComputeSha256Hex(bytes);
                var serialBytes = ImageCropper.TryCropToPngBytes(windowBytes, serialRoiWindow) ?? bytes;
                var serialPath = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(entry.TableName)}_serial_v_{chunkIndex:00}", serialBytes);
                var serialHash = ComputeSha256Hex(serialBytes);
                var scrollbarBytes = ImageCropper.TryCropToPngBytes(windowBytes, scrollbarRoiWindow) ?? bytes;
                var scrollbarHash = ComputeSha256Hex(scrollbarBytes);
                var chunk = new TestlabTableScanChunkResult(chunkIndex, path, roi, frameHash, serialRoi, serialPath, serialHash);
                chunks.Add(chunk);
                return (chunk, scrollbarHash);
            }

            var (currentChunk, currentScrollbarHash) = CaptureChunk(0);
            lastStateHash = $"{currentChunk.SerialSha256}:{currentScrollbarHash}";

            for (var step = 0; step < Math.Max(0, maxSteps - 1); step++)
            {
                var scroll = TryScrollToNextChunk(
                    run,
                    controller,
                    overlay,
                    capturer,
                    screenshots,
                    entry.TableName,
                    win,
                    roi,
                    roiWindow,
                    serialRoi,
                    serialRoiWindow,
                    scrollbarRoi,
                    scrollbarRoiWindow,
                    focusX,
                    focusY,
                    currentChunk.SerialSha256,
                    currentScrollbarHash,
                    step
                );
                scrollEvents.Add(scroll);
                if (!scroll.Changed)
                {
                    break;
                }

                var (nextChunk, nextScrollbarHash) = CaptureChunk(chunks.Count);
                var nextStateHash = $"{nextChunk.SerialSha256}:{nextScrollbarHash}";
                if (string.Equals(lastStateHash, nextStateHash, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                currentChunk = nextChunk;
                currentScrollbarHash = nextScrollbarHash;
                lastStateHash = nextStateHash;
            }

            if (scrollEvents.Count > 0)
            {
                var scrollPath = Path.Combine(run.RunDirectory, $"table_scroll_events_{Normalize(entry.TableName)}.json");
                var scrollReport = new TestlabTableScrollEventsReport(entry.TabName, entry.TableName, roi, chunks[0].SerialRoiScreen, scrollbarRoi, scrollEvents);
                File.WriteAllText(scrollPath, scrollReport.ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            var uniqueChunks = GetUniqueChunksBySerial(chunks);
            var stitchedPath = uniqueChunks.Count > 0
                ? TryWriteStitchedTableEvidence(run, screenshots, entry.TableName, uniqueChunks)
                : null;
            WriteTableEvidenceReport(run, entry.TabName, entry.TableName, chunks, uniqueChunks, scrollEvents, stitchedPath);
            results.Add(new TestlabTableScanResult(entry.TabName, entry.TableName, entry.TableRoiWindow, entry.TableRoiScreen, chunks, uniqueChunks.Count, stitchedPath));
        }

        return results;
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
        TestlabTableEntryResult entry,
        TestlabWindowInfo win,
        BBox roiScreen,
        BBox roiWindow,
        int serialX,
        int serialWidthHint,
        BBox scrollbarRoiScreen,
        BBox scrollbarRoiWindow,
        int focusX,
        int focusY,
        int pauseMs,
        BBox? topSerialVerifyRoiWindow,
        string? topSerialVerifySha256
    )
    {
        if (!RequiresDeterministicTopReset(entry))
        {
            return true;
        }

        controller.ClickScreenPoint(focusX, focusY);
        Thread.Sleep(60);

        var pgUpCount = GetIntEnv("CHECKMIND_TABLE_RESET_TOP_PGUP_COUNT", 10);
        var pgUpRetryCount = GetIntEnv("CHECKMIND_TABLE_RESET_TOP_PGUP_RETRY_COUNT", 5);
        var stableConsecutive = Math.Max(1, GetIntEnv("CHECKMIND_TABLE_RESET_TOP_STABLE_CONSECUTIVE", 2));
        var keyDelayMs = GetIntEnv("CHECKMIND_TABLE_KEY_DELAY_MS", 10);
        var pagePauseMs = GetIntEnv("CHECKMIND_TABLE_PAGE_PAUSE_MS", 25);

        var beforeWindowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
        var beforeTableBytes = ImageCropper.TryCropToPngBytes(beforeWindowBytes, roiWindow) ?? beforeWindowBytes;
        _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(entry.TableName)}_reset_top_before", beforeTableBytes);

        var serialWidth = Math.Clamp(serialWidthHint, 20, Math.Max(20, roiWindow.Width - serialX));
        var serialRoiWindow = new BBox(roiWindow.X + serialX, roiWindow.Y, serialWidth, roiWindow.Height);

        static (string SerialSha, string ScrollbarSha) CaptureResetState(byte[] windowBytes, BBox roiWindow, BBox serialRoiWindow, BBox scrollbarRoiWindow)
        {
            var tableBytes = ImageCropper.TryCropToPngBytes(windowBytes, roiWindow) ?? windowBytes;
            var serialBytes = ImageCropper.TryCropToPngBytes(windowBytes, serialRoiWindow) ?? tableBytes;
            var scrollbarBytes = ImageCropper.TryCropToPngBytes(windowBytes, scrollbarRoiWindow) ?? tableBytes;
            return (ComputeSha256Hex(serialBytes), ComputeSha256Hex(scrollbarBytes));
        }

        var (beforeSerialSha, beforeScrollbarSha) = CaptureResetState(beforeWindowBytes, roiWindow, serialRoiWindow, scrollbarRoiWindow);

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
                controller.PressPageUp(keyDelayMs);
                pressAttempts++;
            }
        }

        bool ProbeTopStable()
        {
            var probeWindowBytes0 = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
            var (probeSerialSha0, probeScrollbarSha0) = CaptureResetState(probeWindowBytes0, roiWindow, serialRoiWindow, scrollbarRoiWindow);
            var lastHash = $"{probeSerialSha0}:{probeScrollbarSha0}";
            var allSame = true;

            currentWindowBytes = probeWindowBytes0;
            currentSerialSha = probeSerialSha0;
            currentScrollbarSha = probeScrollbarSha0;

            for (var i = 0; i < stableConsecutive; i++)
            {
                controller.PressPageUp(keyDelayMs);
                pressAttempts++;
                Thread.Sleep(pagePauseMs);
                var probeWindowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
                var (probeSerialSha, probeScrollbarSha) = CaptureResetState(probeWindowBytes, roiWindow, serialRoiWindow, scrollbarRoiWindow);
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
            serialRoiWindow,
            topSerialVerifyRoiWindow,
            screenshots,
            entry.TableName,
            topSerialVerifySha256
        );
        var finalTopReached = reachedTopStable;

        sw.Stop();
        var elapsedMs = sw.ElapsedMilliseconds;

        var afterTableBytes = ImageCropper.TryCropToPngBytes(currentWindowBytes, roiWindow) ?? currentWindowBytes;
        _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(entry.TableName)}_reset_top_after", afterTableBytes);

        TestlabDebugMarkers.WritePhase(
            "runner.table_reset_top",
            run.RunDirectory,
            $"table={entry.TableName};method=pgup;serialChanged={!string.Equals(beforeSerialSha, currentSerialSha, StringComparison.OrdinalIgnoreCase)};scrollbarChanged={!string.Equals(beforeScrollbarSha, currentScrollbarSha, StringComparison.OrdinalIgnoreCase)};focus=({focusX},{focusY});pgup={pgUpCount};retry={pgUpRetryCount};stableConsecutive={stableConsecutive};stableRun={stableRun};keyDelayMs={keyDelayMs};pagePauseMs={pagePauseMs};pressAttempts={pressAttempts};elapsedMs={elapsedMs};stableByHash={(reachedTopStable ? 1 : 0)};topSignature={(topSignatureMatched ? 1 : 0)};stableTop={(finalTopReached ? 1 : 0)}"
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
        string tableName,
        TestlabWindowInfo win,
        BBox roiScreen,
        BBox roiWindow,
        BBox serialRoiScreen,
        BBox serialRoiWindow,
        BBox scrollbarRoiScreen,
        BBox scrollbarRoiWindow,
        int focusX,
        int focusY,
        string currentSerialSha256,
        string currentScrollbarSha256,
        int step
    )
    {
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

            var windowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
            _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(tableName)}_window_after_v_{step:00}_{method}", windowBytes);
            var roiBytes = ImageCropper.TryCropToPngBytes(windowBytes, roiWindow) ?? windowBytes;
            _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(tableName)}_after_v_{step:00}_{method}", roiBytes);
            _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(tableName)}_serial_after_v_{step:00}_{method}", serialBytes);
            TestlabDebugMarkers.WritePhase(
                "runner.table_scroll_attempt",
                run.RunDirectory,
                $"table={tableName};step={step};method={method};serialChanged={!string.Equals(serialSha, currentSerialSha256, StringComparison.OrdinalIgnoreCase)};scrollbarChanged={!string.Equals(scrollbarSha, currentScrollbarSha256, StringComparison.OrdinalIgnoreCase)};focus=({focusX},{focusY});scrollbar=({scrollbarRoiScreen.X},{scrollbarRoiScreen.Y},{scrollbarRoiScreen.Width},{scrollbarRoiScreen.Height})"
            );
        }

        bool Detect(out string afterSerialSha, out string afterScrollbarSha, out byte[] afterSerialBytes)
        {
            var windowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
            var serialBytes = ImageCropper.TryCropToPngBytes(windowBytes, serialRoiWindow) ?? windowBytes;
            var scrollbarBytes = ImageCropper.TryCropToPngBytes(windowBytes, scrollbarRoiWindow) ?? windowBytes;
            var serialSha = ComputeSha256Hex(serialBytes);
            var scrollbarSha = ComputeSha256Hex(scrollbarBytes);
            afterSerialBytes = serialBytes;
            afterSerialSha = serialSha;
            afterScrollbarSha = scrollbarSha;
            return !string.Equals(serialSha, currentSerialSha256, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(scrollbarSha, currentScrollbarSha256, StringComparison.OrdinalIgnoreCase);
        }

        controller.ClickScreenPoint(focusX, focusY);
        Thread.Sleep(50);
        controller.PressPageDown(keyDelayMs);
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

        var finalWindowBytes = CaptureWithOverlay(overlay, () => capturer.CaptureWindowPngBytes(win.Hwnd));
        var finalBytes = ImageCropper.TryCropToPngBytes(finalWindowBytes, serialRoiWindow) ?? finalWindowBytes;
        var finalSha = ComputeSha256Hex(finalBytes);
        var finalScrollbarBytes = ImageCropper.TryCropToPngBytes(finalWindowBytes, scrollbarRoiWindow) ?? finalWindowBytes;
        var finalBar = ComputeSha256Hex(finalScrollbarBytes);
        _ = screenshots.SaveDebugPng(run, $"testlab_table_{Normalize(tableName)}_serial_after_v_{step:00}_fail", finalBytes);
        TestlabDebugMarkers.WritePhase(
            "runner.table_scroll_no_change",
            run.RunDirectory,
            $"table={tableName};step={step};method=pgdn;keyDelayMs={keyDelayMs};pagePauseMs={pagePauseMs}"
        );
        return Make("fail", finalSha, finalBar, false);
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static void WriteCoverage(RunContext run, IReadOnlyList<TestlabTableScanResult> scans)
    {
        var json = JsonSerializer.Serialize(scans, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(run.RunDirectory, "coverage.json"), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static IReadOnlyList<TestlabTableScanResult> GetAllTableScans(IReadOnlyList<TestlabTabSwitchResult> switches)
    {
        var scans = new List<TestlabTableScanResult>();
        foreach (var item in switches)
        {
            if (item.TableScans is null || item.TableScans.Count == 0)
            {
                continue;
            }

            scans.AddRange(item.TableScans);
        }

        return scans;
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
               (normalizedTab == Normalize("Channel Setup") && normalizedTable == Normalize("Channel Setup Table"));
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
    IReadOnlyList<TestlabTabSwitchResult> TabSwitches
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
    IReadOnlyList<TestlabTableScanResult>? TableScans = null
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
    BBox TableRoiScreen
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
