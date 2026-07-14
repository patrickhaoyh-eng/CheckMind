using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CheckMind.App.Core;

public sealed class TestlabPreflightCalibrationChecker
{
    public void CheckAndThrow(RunContext run, TestlabWindowInfo win, string[] tabs)
    {
        var enforceCaptureRoiRaw = (Environment.GetEnvironmentVariable("CHECKMIND_PREFLIGHT_REQUIRE_CAPTURE_ROI") ?? "").Trim();
        var enforceCaptureRoi = string.Equals(enforceCaptureRoiRaw, "1", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(enforceCaptureRoiRaw, "true", StringComparison.OrdinalIgnoreCase);

        var store = WorkstationProfileStore.CreateDefault();
        if (!File.Exists(store.ProfilePath))
        {
            var reportPath0 = Path.Combine(run.RunDirectory, "preflight_calibration_report.json");
            var report0 = new PreflightCalibrationReport(
                IsCompliant: false,
                ProfilePath: store.ProfilePath,
                Tabs: tabs,
                Failures:
                [
                    new PreflightCalibrationFailure(
                        Key: "profile_missing",
                        Message: "未找到固定工位 profile 配置文件。",
                        Expected: store.ProfilePath,
                        Actual: null,
                        Suggestion: "请先完成标定并生成 profile。"
                    )
                ]
            );
            File.WriteAllText(reportPath0, report0.ToJson(), new UTF8Encoding(false));
            throw new PreflightCalibrationGateException("人工标定运行前核验失败：未找到 profile。", reportPath0);
        }

        var profile = store.Load();
        var sw = Stopwatch.StartNew();

        var failures = new List<PreflightCalibrationFailure>();
        var warnings = new List<PreflightCalibrationFailure>();
        var w = win.Rect.Width;
        var h = win.Rect.Height;

        foreach (var tab in tabs)
        {
            if (string.IsNullOrWhiteSpace(tab))
            {
                continue;
            }

            var click = profile.FindTabClickTarget(tab)?.ClickPoint;
            if (click is null)
            {
                failures.Add(new PreflightCalibrationFailure(
                    Key: "tab_click_point_missing",
                    Message: $"缺少页签 ClickPoint：{tab}",
                    Expected: "ClickPoint(window) configured",
                    Actual: "null",
                    Suggestion: "请先执行页签 ClickPoint 标定。"
                ));
            }
            else
            {
                if (click.Value.X < 0 || click.Value.Y < 0 || click.Value.X >= w || click.Value.Y >= h)
                {
                    failures.Add(new PreflightCalibrationFailure(
                        Key: "tab_click_point_out_of_window",
                        Message: $"页签 ClickPoint 不在窗口内：{tab}",
                        Expected: $"0<=x<{w}, 0<=y<{h}",
                        Actual: $"({click.Value.X},{click.Value.Y})",
                        Suggestion: "请重新标定 ClickPoint，确保在 Testlab 窗口内。"
                    ));
                }
            }

            var page = profile.FindPageProfile(tab);
            if (page is null)
            {
                failures.Add(new PreflightCalibrationFailure(
                    Key: "page_profile_missing",
                    Message: $"缺少页面 profile：{tab}",
                    Expected: "Pages[] contains tab entry",
                    Actual: "null",
                    Suggestion: "请更新 profile，确保包含该页签的页面配置。"
                ));
                continue;
            }

            var verify = page.FindVerifyTarget("tab_verify");
            if (verify is null || verify.RoiWindow is null || string.IsNullOrWhiteSpace(verify.Sha256))
            {
                failures.Add(new PreflightCalibrationFailure(
                    Key: "tab_verify_missing",
                    Message: $"缺少页签验真签名：{tab}",
                    Expected: "VerifyRoiWindow + VerifySha256",
                    Actual: "null",
                    Suggestion: "请先执行验真签名标定。"
                ));
            }
            else
            {
                var roi = verify.RoiWindow.Value;
                if (roi.X < 0 || roi.Y < 0 || roi.Width <= 0 || roi.Height <= 0 || (roi.X + roi.Width) > w || (roi.Y + roi.Height) > h)
                {
                    failures.Add(new PreflightCalibrationFailure(
                        Key: "tab_verify_roi_out_of_window",
                        Message: $"页签验真 ROI 不在窗口内：{tab}",
                        Expected: $"roi within 0..{w}x{h}",
                        Actual: $"({roi.X},{roi.Y},{roi.Width},{roi.Height})",
                        Suggestion: "请重新标定验真 ROI，确保在 Testlab 窗口范围内。"
                    ));
                }
            }

            var entryCapture = page.FindCaptureTarget("table_entry");
            var scanCapture = page.FindCaptureTarget("table_scan");
            if (entryCapture is null || entryCapture.RoiWindow is null)
            {
                (enforceCaptureRoi ? failures : warnings).Add(new PreflightCalibrationFailure(
                    Key: "capture_roi_entry_missing",
                    Message: $"缺少表格入口截图框（table_entry）：{tab}",
                    Expected: "CaptureTargets.table_entry.RoiWindow configured",
                    Actual: "null",
                    Suggestion: "请在前端点击“标定截图框”，先完成表格入口截图框标定后再执行。"
                ));
            }
            else
            {
                var roi = entryCapture.RoiWindow.Value;
                if (roi.X < 0 || roi.Y < 0 || roi.Width <= 0 || roi.Height <= 0 || (roi.X + roi.Width) > w || (roi.Y + roi.Height) > h)
                {
                    failures.Add(new PreflightCalibrationFailure(
                        Key: "capture_roi_entry_out_of_window",
                        Message: $"表格入口截图框不在窗口内（table_entry）：{tab}",
                        Expected: $"roi within 0..{w}x{h}",
                        Actual: $"({roi.X},{roi.Y},{roi.Width},{roi.Height})",
                        Suggestion: "请在前端重新标定表格入口截图框，确保在 Testlab 窗口范围内。"
                    ));
                }
            }

            if (scanCapture is null || scanCapture.RoiWindow is null)
            {
                (enforceCaptureRoi ? failures : warnings).Add(new PreflightCalibrationFailure(
                    Key: "capture_roi_scan_missing",
                    Message: $"缺少表格滑窗截图框（table_scan）：{tab}",
                    Expected: "CaptureTargets.table_scan.RoiWindow configured",
                    Actual: "null",
                    Suggestion: "请在前端点击“标定截图框”，先完成表格滑窗截图框标定后再执行。"
                ));
            }
            else
            {
                var roi = scanCapture.RoiWindow.Value;
                if (roi.X < 0 || roi.Y < 0 || roi.Width <= 0 || roi.Height <= 0 || (roi.X + roi.Width) > w || (roi.Y + roi.Height) > h)
                {
                    failures.Add(new PreflightCalibrationFailure(
                        Key: "capture_roi_scan_out_of_window",
                        Message: $"表格滑窗截图框不在窗口内（table_scan）：{tab}",
                        Expected: $"roi within 0..{w}x{h}",
                        Actual: $"({roi.X},{roi.Y},{roi.Width},{roi.Height})",
                        Suggestion: "请在前端重新标定表格滑窗截图框，确保在 Testlab 窗口范围内。"
                    ));
                }
            }
        }

        sw.Stop();
        var reportPath = Path.Combine(run.RunDirectory, "preflight_calibration_report.json");
        var report = new PreflightCalibrationReport(
            IsCompliant: failures.Count == 0,
            ProfilePath: store.ProfilePath,
            Tabs: tabs,
            Failures: failures.ToArray(),
            Warnings: warnings.Count == 0 ? null : warnings.ToArray()
        );
        File.WriteAllText(reportPath, report.ToJson(), new UTF8Encoding(false));

        TestlabDebugMarkers.WritePhase(
            "runner.preflight_calibration_summary",
            run.RunDirectory,
            $"enforceCaptureRoi={(enforceCaptureRoi ? 1 : 0)};failures={failures.Count};warnings={warnings.Count};report={reportPath}"
        );

        if (failures.Count > 0)
        {
            throw new PreflightCalibrationGateException("人工标定运行前核验失败：请先修复标定项后再执行。", reportPath);
        }
    }
}
