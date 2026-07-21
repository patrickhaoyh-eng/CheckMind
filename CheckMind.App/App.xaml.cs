using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CheckMind.App.Core;
using CheckMind.App.Ui;

namespace CheckMind.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static bool ShouldSuppressAutomationDialogs()
    {
        var noDialogs = (Environment.GetEnvironmentVariable("CHECKMIND_EMBEDDED_NO_DIALOGS") ?? "").Trim();
        return string.Equals(noDialogs, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(noDialogs, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void ShowAutomationMessage(string message, string caption, MessageBoxImage image = MessageBoxImage.Warning)
    {
        if (ShouldSuppressAutomationDialogs())
        {
            return;
        }

        System.Windows.MessageBox.Show(
            message,
            caption,
            MessageBoxButton.OK,
            image
        );
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var dpiBootstrap = TrySetPerMonitorV2DpiAwareness();
        base.OnStartup(e);

        var debugEnv = Environment.GetEnvironmentVariable("CHECKMIND_DEBUG_ENV");
        if (string.Equals(debugEnv, "1", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var dumpPath = Path.Combine(Path.GetTempPath(), "checkmind_env_dump.txt");
                File.WriteAllText(
                    dumpPath,
                    $"CHECKMIND_RUN_SAMPLE={Environment.GetEnvironmentVariable("CHECKMIND_RUN_SAMPLE")}{Environment.NewLine}" +
                    $"CHECKMIND_RUN_TESTLAB={Environment.GetEnvironmentVariable("CHECKMIND_RUN_TESTLAB")}{Environment.NewLine}" +
                    $"CHECKMIND_SAMPLE_DIR={Environment.GetEnvironmentVariable("CHECKMIND_SAMPLE_DIR")}{Environment.NewLine}" +
                    $"CHECKMIND_OCR_BASE_URL={Environment.GetEnvironmentVariable("CHECKMIND_OCR_BASE_URL")}{Environment.NewLine}" +
                    $"CHECKMIND_OCR_MODEL_ID={Environment.GetEnvironmentVariable("CHECKMIND_OCR_MODEL_ID")}{Environment.NewLine}" +
                    $"CHECKMIND_OCR_API_KEY_ENV={Environment.GetEnvironmentVariable("CHECKMIND_OCR_API_KEY_ENV")}{Environment.NewLine}" +
                    $"CHECKMIND_OCR_MODE={Environment.GetEnvironmentVariable("CHECKMIND_OCR_MODE")}{Environment.NewLine}" +
                    $"CHECKMIND_TESTLAB_PROCESS={Environment.GetEnvironmentVariable("CHECKMIND_TESTLAB_PROCESS")}{Environment.NewLine}" +
                    $"CHECKMIND_TESTLAB_TITLE_CONTAINS={Environment.GetEnvironmentVariable("CHECKMIND_TESTLAB_TITLE_CONTAINS")}{Environment.NewLine}" +
                    $"CHECKMIND_TESTLAB_TABS={Environment.GetEnvironmentVariable("CHECKMIND_TESTLAB_TABS")}{Environment.NewLine}" +
                    $"CHECKMIND_OVERLAY={Environment.GetEnvironmentVariable("CHECKMIND_OVERLAY")}{Environment.NewLine}"
                );
            }
            catch
            {
            }
        }

        var runSample = Environment.GetEnvironmentVariable("CHECKMIND_RUN_SAMPLE");
        var sampleDir = Environment.GetEnvironmentVariable("CHECKMIND_SAMPLE_DIR");
        if (string.Equals(runSample, "1", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(sampleDir))
        {
            try
            {
                var run = new RunStorage().CreateRun(new RunCreateOptions(TrialId: "SAMPLE"));
                var extractedPath = Task.Run(() => new SampleExtractionRunner().RunAsync(run, sampleDir)).GetAwaiter().GetResult();

                var markerPath = Path.Combine(Path.GetTempPath(), "checkmind_sample_last_run.txt");
                File.WriteAllText(markerPath, $"{run.RunDirectory}{Environment.NewLine}{extractedPath}");
            }
            catch (Exception ex)
            {
                var errorPath = Path.Combine(Path.GetTempPath(), "checkmind_sample_error.txt");
                File.WriteAllText(errorPath, ex.ToString());
            }
            finally
            {
                Shutdown();
            }

            return;
        }

        var runTestlab = Environment.GetEnvironmentVariable("CHECKMIND_RUN_TESTLAB");
        if (string.Equals(runTestlab, "1", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var cfgStore = new AppConfigStore();
            var cfg = cfgStore.LoadOrCreateDefault();
            var uiCfg = cfg.AutomationUi ?? new AutomationUiConfig();
            var consentPromptEnabled = !uiCfg.SuppressMouseCapturePrompt;
            var promptEnv = Environment.GetEnvironmentVariable("CHECKMIND_CAPTURE_PROMPT");
            if (!string.IsNullOrWhiteSpace(promptEnv))
            {
                consentPromptEnabled = !string.Equals(promptEnv.Trim(), "0", StringComparison.OrdinalIgnoreCase) &&
                                       !string.Equals(promptEnv.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            }

            var consentPromptEnv = Environment.GetEnvironmentVariable("CHECKMIND_CAPTURE_CONSENT_PROMPT");
            if (!string.IsNullOrWhiteSpace(consentPromptEnv))
            {
                consentPromptEnabled = !string.Equals(consentPromptEnv.Trim(), "0", StringComparison.OrdinalIgnoreCase) &&
                                       !string.Equals(consentPromptEnv.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            }

            var finishedPromptEnabled = !uiCfg.SuppressCaptureFinishedPrompt;
            var finishedPromptEnv = Environment.GetEnvironmentVariable("CHECKMIND_CAPTURE_FINISHED_PROMPT");
            if (!string.IsNullOrWhiteSpace(finishedPromptEnv))
            {
                finishedPromptEnabled = !string.Equals(finishedPromptEnv.Trim(), "0", StringComparison.OrdinalIgnoreCase) &&
                                        !string.Equals(finishedPromptEnv.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            }

            var overlayEnabled = uiCfg.OverlayEnabled;
            var overlayEnv = Environment.GetEnvironmentVariable("CHECKMIND_OVERLAY");
            if (!string.IsNullOrWhiteSpace(overlayEnv))
            {
                overlayEnabled = string.Equals(overlayEnv.Trim(), "1", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(overlayEnv.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            }

            _ = RunTestlabAsync(cfgStore, cfg, consentPromptEnabled, finishedPromptEnabled, overlayEnabled);
            return;

            async Task RunTestlabAsync(AppConfigStore cfgStore, AppConfig cfg, bool consentPromptEnabled, bool finishedPromptEnabled, bool overlayEnabled)
            {
                CaptureOverlayService? overlay = null;
                string? runDirectory = null;
                try
                {
                    var taskRequest = TaskContractResolver.ResolveFromEnvironment();
                    var run = new RunStorage().CreateRun(new RunCreateOptions(
                        TrialId: "TESTLAB",
                        TaskRequest: taskRequest
                    ));
                    runDirectory = run.RunDirectory;
                    TestlabDebugMarkers.SetCurrentRunDirectory(runDirectory);
                    TestlabDebugMarkers.WritePhase("app.dpi_bootstrap", runDirectory, dpiBootstrap);
                    var markerPath = Path.Combine(Path.GetTempPath(), "checkmind_testlab_last_run.txt");
                    File.WriteAllText(markerPath, run.RunDirectory);
                    TestlabDebugMarkers.WritePhase("app.run_created", runDirectory);

                    if (consentPromptEnabled)
                    {
                        TestlabDebugMarkers.WritePhase("app.before_consent_dialog", runDirectory);
                        var consent = new CaptureConsentDialog();
                        _ = consent.ShowDialog();
                        TestlabDebugMarkers.WritePhase(
                            "app.after_consent_dialog",
                            runDirectory,
                            consent.Accepted ? "accepted" : "canceled"
                        );
                        if (!consent.Accepted)
                        {
                            var errorPath = Path.Combine(Path.GetTempPath(), "checkmind_testlab_error.txt");
                            File.WriteAllText(errorPath, "CanceledByUser");
                            TestlabDebugMarkers.WritePhase("app.user_canceled", runDirectory);
                            return;
                        }

                        if (consent.RememberChoice)
                        {
                            var nextUi = (cfg.AutomationUi ?? new AutomationUiConfig()) with { SuppressMouseCapturePrompt = true };
                            cfgStore.Save(cfg with { AutomationUi = nextUi });
                            consentPromptEnabled = false;
                        }
                    }

                    var calibrateEnv = (Environment.GetEnvironmentVariable("CHECKMIND_CALIBRATE_TAB_CLICKPOINTS") ?? "").Trim();
                    var calibrateEnabled = string.Equals(calibrateEnv, "1", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(calibrateEnv, "true", StringComparison.OrdinalIgnoreCase);
                    if (calibrateEnabled)
                    {
                        var tabsRaw = Environment.GetEnvironmentVariable("CHECKMIND_TESTLAB_TABS");
                        var tabs = string.IsNullOrWhiteSpace(tabsRaw)
                            ? Array.Empty<string>()
                            : tabsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        TestlabDebugMarkers.WritePhase("app.before_calibrate_tabs", runDirectory, $"tabs={string.Join(",", tabs)}");
                        await new TestlabTabClickPointCalibrator().CalibrateAsync(run, tabs);
                        TestlabDebugMarkers.WritePhase("app.after_calibrate_tabs", runDirectory);
                        return;
                    }

                    var calibrateCaptureEnv = (Environment.GetEnvironmentVariable("CHECKMIND_CALIBRATE_CAPTURE_ROI") ?? "").Trim();
                    var calibrateCaptureEnabled = string.Equals(calibrateCaptureEnv, "1", StringComparison.OrdinalIgnoreCase) ||
                                                  string.Equals(calibrateCaptureEnv, "true", StringComparison.OrdinalIgnoreCase);
                    if (calibrateCaptureEnabled)
                    {
                        var tabsRaw = Environment.GetEnvironmentVariable("CHECKMIND_TESTLAB_TABS");
                        var tabs = string.IsNullOrWhiteSpace(tabsRaw)
                            ? Array.Empty<string>()
                            : tabsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        TestlabDebugMarkers.WritePhase("app.before_calibrate_capture_roi", runDirectory, $"tabs={string.Join(",", tabs)}");
                        await new TestlabCaptureRoiCalibrator().CalibrateAsync(run, tabs);
                        TestlabDebugMarkers.WritePhase("app.after_calibrate_capture_roi", runDirectory);
                        return;
                    }

                    var calibrateChildWindowEnv = (Environment.GetEnvironmentVariable("CHECKMIND_CALIBRATE_CHILD_WINDOW") ?? "").Trim();
                    var calibrateChildWindowEnabled = string.Equals(calibrateChildWindowEnv, "1", StringComparison.OrdinalIgnoreCase) ||
                                                      string.Equals(calibrateChildWindowEnv, "true", StringComparison.OrdinalIgnoreCase);
                    if (calibrateChildWindowEnabled)
                    {
                        var childWindowKey = (Environment.GetEnvironmentVariable("CHECKMIND_CHILD_WINDOW_KEY") ?? "").Trim();
                        TestlabDebugMarkers.WritePhase("app.before_calibrate_child_window", runDirectory, $"key={childWindowKey}");
                        switch (childWindowKey.ToLowerInvariant())
                        {
                            case "define_notch_profiles":
                                await new TestlabChildWindowCalibrator().CalibrateDefineNotchProfilesAsync(run);
                                break;
                            case "define_notch_profiles_layout_signature":
                                await new TestlabChildWindowCalibrator().CalibrateDefineNotchProfilesLayoutSignatureAsync(run);
                                break;
                            case "sine_setup_channel_safety_parameters":
                                await new TestlabChildWindowCalibrator().CalibrateSineSetupChannelSafetyParametersAsync(run);
                                break;
                            case "notch_profile":
                                await new TestlabChildWindowCalibrator().CalibrateNotchProfileAsync(run);
                                break;
                            case "notch_profile_paging_focus":
                                await new TestlabChildWindowCalibrator().CalibrateNotchProfilePagingFocusAsync(run);
                                break;
                            case "notch_profile_paging_activation":
                                await new TestlabChildWindowCalibrator().CalibrateNotchProfilePagingActivationAsync(run);
                                break;
                            case "profile_editor":
                                await new TestlabChildWindowCalibrator().CalibrateProfileEditorAsync(run);
                                break;
                            case "profile_editor_paging_focus":
                                await new TestlabChildWindowCalibrator().CalibrateProfileEditorPagingFocusAsync(run);
                                break;
                            case "profile_editor_paging_activation":
                                await new TestlabChildWindowCalibrator().CalibrateProfileEditorPagingActivationAsync(run);
                                break;
                            case "profile_editor_top_signature":
                                await new TestlabChildWindowCalibrator().CalibrateProfileEditorTopSignatureAsync(run);
                                break;
                            case "advanced_control_setup":
                                await new TestlabChildWindowCalibrator().CalibrateAdvancedControlSetupAsync(run);
                                break;
                            default:
                                throw new InvalidOperationException($"暂不支持的子窗口标定 key：{childWindowKey}");
                        }

                        TestlabDebugMarkers.WritePhase("app.after_calibrate_child_window", runDirectory, $"key={childWindowKey}");
                        return;
                    }

                    var notchEntryProbeEnv = (Environment.GetEnvironmentVariable("CHECKMIND_RUN_NOTCH_ENTRY_PROBE") ?? "").Trim();
                    var notchEntryProbeEnabled = string.Equals(notchEntryProbeEnv, "1", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(notchEntryProbeEnv, "true", StringComparison.OrdinalIgnoreCase);
                    if (notchEntryProbeEnabled)
                    {
                        TestlabDebugMarkers.WritePhase("app.before_notch_entry_probe", runDirectory);
                        _ = await Task.Run(() => new NotchProfilesEntryProbe().Run(run));
                        TestlabDebugMarkers.WritePhase("app.after_notch_entry_probe", runDirectory);
                        return;
                    }

                    var notchTableScanProbeEnv = (Environment.GetEnvironmentVariable("CHECKMIND_RUN_NOTCH_TABLE_SCAN_PROBE") ?? "").Trim();
                    var notchTableScanProbeEnabled = string.Equals(notchTableScanProbeEnv, "1", StringComparison.OrdinalIgnoreCase) ||
                                                     string.Equals(notchTableScanProbeEnv, "true", StringComparison.OrdinalIgnoreCase);
                    if (notchTableScanProbeEnabled)
                    {
                        TestlabDebugMarkers.WritePhase("app.before_notch_table_scan_probe", runDirectory);
                        _ = await Task.Run(() => new NotchProfileTableScanProbe().Run(run));
                        TestlabDebugMarkers.WritePhase("app.after_notch_table_scan_probe", runDirectory);
                        return;
                    }

                    var notchManualPgdnProbeEnv = (Environment.GetEnvironmentVariable("CHECKMIND_RUN_NOTCH_MANUAL_PGDN_PROBE") ?? "").Trim();
                    var notchManualPgdnProbeEnabled = string.Equals(notchManualPgdnProbeEnv, "1", StringComparison.OrdinalIgnoreCase) ||
                                                      string.Equals(notchManualPgdnProbeEnv, "true", StringComparison.OrdinalIgnoreCase);
                    if (notchManualPgdnProbeEnabled)
                    {
                        TestlabDebugMarkers.WritePhase("app.before_notch_manual_pgdn_probe", runDirectory);
                        _ = new NotchProfileManualPgdnProbe().Run(run);
                        TestlabDebugMarkers.WritePhase("app.after_notch_manual_pgdn_probe", runDirectory);
                        return;
                    }

                    var notchKeyModeProbeEnv = (Environment.GetEnvironmentVariable("CHECKMIND_RUN_NOTCH_KEY_MODE_PROBE") ?? "").Trim();
                    var notchKeyModeProbeEnabled = string.Equals(notchKeyModeProbeEnv, "1", StringComparison.OrdinalIgnoreCase) ||
                                                   string.Equals(notchKeyModeProbeEnv, "true", StringComparison.OrdinalIgnoreCase);
                    if (notchKeyModeProbeEnabled)
                    {
                        TestlabDebugMarkers.WritePhase("app.before_notch_key_mode_probe", runDirectory);
                        _ = new NotchProfileKeyModeProbe().Run(run);
                        TestlabDebugMarkers.WritePhase("app.after_notch_key_mode_probe", runDirectory);
                        return;
                    }

                    if (overlayEnabled)
                    {
                        TestlabDebugMarkers.WritePhase("app.before_overlay_start", runDirectory);
                        overlay = new CaptureOverlayService();
                        overlay.Start();
                        overlay.SetVisible(true);
                        TestlabDebugMarkers.WritePhase("app.after_overlay_start", runDirectory);
                    }

                    TestlabDebugMarkers.WritePhase("app.before_runner_run", runDirectory);
                    var testlabRunResult = await Task.Run(() => new TestlabAutomationRunner().Run(run, overlay));
                    new RunResultsStore().SaveTestlabResult(run, taskRequest, testlabRunResult);
                    TestlabDebugMarkers.WritePhase("app.after_runner_run", runDirectory);
                    Environment.ExitCode = 0;

                    if (finishedPromptEnabled)
                    {
                        TestlabDebugMarkers.WritePhase("app.before_finished_dialog", runDirectory);
                        var done = new CaptureFinishedDialog(run.RunDirectory);
                        _ = done.ShowDialog();
                        TestlabDebugMarkers.WritePhase(
                            "app.after_finished_dialog",
                            runDirectory,
                            done.OpenRunDirectory ? "open-run-directory" : "close"
                        );
                        if (done.OpenRunDirectory)
                        {
                            try
                            {
                                var dir = (run.RunDirectory ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                                {
                                    _ = Process.Start(new ProcessStartInfo
                                    {
                                        FileName = dir,
                                        UseShellExecute = true,
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                TestlabDebugMarkers.WritePhase("app.open_run_directory_failed", runDirectory, $"{ex.GetType().Name}:{ex.Message}");
                                ShowAutomationMessage(
                                    $"无法自动打开保存路径，请手动打开：\r\n{run.RunDirectory}",
                                    "CheckMind - 打开保存路径失败",
                                    MessageBoxImage.Warning
                                );
                            }
                        }
                    }
                }
                catch (TestlabWindowStateException ex)
                {
                    Environment.ExitCode = 2;
                    TestlabDebugMarkers.WritePhase("app.window_state_exception", runDirectory, ex.Message);
                    var errorPath = Path.Combine(Path.GetTempPath(), "checkmind_testlab_error.txt");
                    File.WriteAllText(errorPath, ex.ToString());
                    ShowAutomationMessage(
                        ex.Message,
                        "CheckMind - Testlab 窗口状态异常",
                        MessageBoxImage.Warning
                    );
                }
                catch (WorkstationNotCompliantException ex)
                {
                    Environment.ExitCode = 4;
                    TestlabDebugMarkers.WritePhase("app.workstation_not_compliant", runDirectory, ex.Message);
                    var errorPath = Path.Combine(Path.GetTempPath(), "checkmind_testlab_error.txt");
                    File.WriteAllText(errorPath, ex.ToString());

                    var message = ex.Message;
                    try
                    {
                        var json = File.ReadAllText(ex.ReportPath);
                        var report = System.Text.Json.JsonSerializer.Deserialize<WorkstationComplianceReport>(json, JsonOptions.Default);
                        if (report is not null && report.FailedChecks.Count > 0)
                        {
                            var sb = new StringBuilder();
                            _ = sb.AppendLine(ex.Message);
                            _ = sb.AppendLine();
                            _ = sb.AppendLine("不符合项：");
                            foreach (var f in report.FailedChecks)
                            {
                                _ = sb.Append("- ").Append(f.Message);
                                if (!string.IsNullOrWhiteSpace(f.Expected) || !string.IsNullOrWhiteSpace(f.Actual))
                                {
                                    _ = sb.Append("  (期望=").Append(f.Expected ?? "").Append(" 实际=").Append(f.Actual ?? "").Append(')');
                                }

                                _ = sb.AppendLine();
                                if (!string.IsNullOrWhiteSpace(f.Suggestion))
                                {
                                    _ = sb.Append("  建议：").AppendLine(f.Suggestion);
                                }
                            }

                            _ = sb.AppendLine();
                            _ = sb.Append("详情：").AppendLine(ex.ReportPath);
                            message = sb.ToString();
                        }
                    }
                    catch
                    {
                    }

                    ShowAutomationMessage(
                        message,
                        "CheckMind - 固定工位环境不合规",
                        MessageBoxImage.Warning
                    );
                }
                catch (PreflightCalibrationGateException ex)
                {
                    Environment.ExitCode = 4;
                    TestlabDebugMarkers.WritePhase("app.preflight_calibration_gate_failed", runDirectory, ex.Message);
                    var errorPath = Path.Combine(Path.GetTempPath(), "checkmind_testlab_error.txt");
                    File.WriteAllText(errorPath, ex.ToString());

                    var message = ex.Message;
                    try
                    {
                        var json = File.ReadAllText(ex.ReportPath);
                        var report = System.Text.Json.JsonSerializer.Deserialize<PreflightCalibrationReport>(json, JsonOptions.Default);
                        if (report is not null)
                        {
                            var sb = new StringBuilder();
                            _ = sb.AppendLine(ex.Message);
                            _ = sb.AppendLine();
                            _ = sb.Append("Profile：").AppendLine(report.ProfilePath);
                            _ = sb.Append("Tabs：").AppendLine(string.Join(",", report.Tabs ?? []));
                            if (report.Failures is not null && report.Failures.Length > 0)
                            {
                                _ = sb.AppendLine();
                                _ = sb.AppendLine("不通过项：");
                                foreach (var f in report.Failures)
                                {
                                    _ = sb.Append("- ").Append(f.Message);
                                    if (!string.IsNullOrWhiteSpace(f.Expected) || !string.IsNullOrWhiteSpace(f.Actual))
                                    {
                                        _ = sb.Append("  (期望=").Append(f.Expected ?? "").Append(" 实际=").Append(f.Actual ?? "").Append(')');
                                    }
                                    _ = sb.AppendLine();
                                    if (!string.IsNullOrWhiteSpace(f.Suggestion))
                                    {
                                        _ = sb.Append("  建议：").AppendLine(f.Suggestion);
                                    }
                                }
                            }
                            _ = sb.AppendLine();
                            _ = sb.Append("详情：").AppendLine(ex.ReportPath);
                            message = sb.ToString();
                        }
                    }
                    catch
                    {
                    }

                    ShowAutomationMessage(
                        message,
                        "CheckMind - 标定核验失败",
                        MessageBoxImage.Warning
                    );
                }
                catch (TabClickPointGateException ex)
                {
                    Environment.ExitCode = 4;
                    TestlabDebugMarkers.WritePhase("app.tab_click_point_gate_failed", runDirectory, ex.Message);
                    var errorPath = Path.Combine(Path.GetTempPath(), "checkmind_testlab_error.txt");
                    File.WriteAllText(errorPath, ex.ToString());

                    var message = ex.Message;
                    try
                    {
                        var json = File.ReadAllText(ex.ReportPath);
                        var report = System.Text.Json.JsonSerializer.Deserialize<TabClickPointGateReport>(json, JsonOptions.Default);
                        if (report is not null)
                        {
                            var sb = new StringBuilder();
                            _ = sb.AppendLine(report.Message);
                            _ = sb.AppendLine();
                            _ = sb.Append("规则：").AppendLine(report.RuleKey);
                            _ = sb.Append("页签：").AppendLine(report.TabName);
                            if (report.ClickPointWindow is WindowPoint p)
                            {
                                _ = sb.Append("ClickPoint(window)：").AppendLine($"({p.X},{p.Y})");
                            }
                            _ = sb.Append("窗口大小：").AppendLine($"{report.WindowWidth}x{report.WindowHeight}");
                            if (!string.IsNullOrWhiteSpace(report.SuggestedOcrPath) || report.SuggestedClickPointWindow is not null)
                            {
                                _ = sb.AppendLine();
                                _ = sb.AppendLine("建议：");
                                if (report.SuggestedClickPointWindow is WindowPoint sp)
                                {
                                    _ = sb.Append("  建议 ClickPoint(window)：").AppendLine($"({sp.X},{sp.Y})");
                                }
                                if (!string.IsNullOrWhiteSpace(report.SuggestedOcrPath))
                                {
                                    _ = sb.Append("  OCR 证据：").AppendLine(report.SuggestedOcrPath);
                                }
                            }

                            _ = sb.AppendLine();
                            _ = sb.Append("详情：").AppendLine(ex.ReportPath);
                            message = sb.ToString();
                        }
                    }
                    catch
                    {
                    }

                    ShowAutomationMessage(
                        message,
                        "CheckMind - 页签固定点击点门禁失败",
                        MessageBoxImage.Warning
                    );
                }
                catch (Exception ex)
                {
                    Environment.ExitCode = 5;
                    TestlabDebugMarkers.WritePhase("app.unhandled_exception", runDirectory, ex.GetType().Name);
                    var errorPath = Path.Combine(Path.GetTempPath(), "checkmind_testlab_error.txt");
                    File.WriteAllText(errorPath, ex.ToString());
                }
                finally
                {
                    TestlabDebugMarkers.WritePhase("app.finally_begin", runDirectory);
                    try
                    {
                        overlay?.SetRect(null);
                        overlay?.SetVisible(false);
                        overlay?.Dispose();
                    }
                    catch
                    {
                    }
                    TestlabDebugMarkers.WritePhase("app.finally_end", runDirectory);
                    TestlabDebugMarkers.SetCurrentRunDirectory(null);
                    Shutdown();
                }
            }
        }

        var selfTest = Environment.GetEnvironmentVariable("CHECKMIND_SELFTEST");
        if (!string.Equals(selfTest, "1", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var run = new RunStorage().CreateRun(new RunCreateOptions(TrialId: "SELFTEST"));
            var cfg = new AppConfigStore().LoadOrCreateDefault();

            var tmpInputPath = Path.Combine(Path.GetTempPath(), "checkmind_selftest_input.txt");
            File.WriteAllText(tmpInputPath, DateTimeOffset.UtcNow.ToString("O"));
            var inputRef = new RunInputManager().AddFileToRunInputs(run, tmpInputPath, "selftest");
            new RunMetaStore().AppendInput(run, inputRef);

            var ocr = new MockOcrAdapter().RecognizeAsync(
                new OcrRequest(
                    ImageBytes: new byte[] { 1, 2, 3 },
                    ImageMime: "image/png",
                    Roi: new BBox(0, 0, 100, 100),
                    Hint: "selftest"
                )
            ).GetAwaiter().GetResult();
            var ocrPath = new OcrStore().Save(run, "selftest_ocr", ocr);

            var markerPath = Path.Combine(Path.GetTempPath(), "checkmind_selftest_last_run.txt");
            File.WriteAllText(
                markerPath,
                $"{run.RunDirectory}{Environment.NewLine}{new AppConfigStore().ConfigPath}{Environment.NewLine}{inputRef.StoredPath}{Environment.NewLine}{ocrPath}"
            );
        }
        catch (Exception ex)
        {
            var errorPath = Path.Combine(Path.GetTempPath(), "checkmind_selftest_error.txt");
            File.WriteAllText(errorPath, ex.ToString());
        }
        finally
        {
            Shutdown();
        }
    }

    private static string TrySetPerMonitorV2DpiAwareness()
    {
        try
        {
            var setOk = Win32Native.SetProcessDpiAwarenessContext(Win32Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            var lastError = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            var threadContext = Win32Native.GetThreadDpiAwarenessContext();
            var awareness = Win32Native.GetAwarenessFromDpiAwarenessContext(threadContext);
            return $"setOk={setOk};lastError={lastError};threadContext=0x{threadContext.ToInt64():X};awareness={awareness}";
        }
        catch
        {
            return "exception";
        }
    }
}
