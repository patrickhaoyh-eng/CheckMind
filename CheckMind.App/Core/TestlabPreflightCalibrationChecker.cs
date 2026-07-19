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
                    Expected: "VerifyTargets.tab_verify (RoiWindow + Sha256) configured",
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

        CheckDefineNotchProfilesListTarget(profile, w, h, failures);

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

    private static void CheckDefineNotchProfilesListTarget(
        WorkstationProfile profile,
        int windowWidth,
        int windowHeight,
        List<PreflightCalibrationFailure> failures
    )
    {
        var defineWindow = profile.FindChildWindowProfile("define_notch_profiles");
        if (defineWindow is null)
        {
            failures.Add(new PreflightCalibrationFailure(
                Key: "define_notch_profiles_profile_missing",
                Message: "缺少子窗口 profile：define_notch_profiles",
                Expected: "ChildWindows[] contains define_notch_profiles",
                Actual: "null",
                Suggestion: "请先完成 Define notch profiles 标定。"
            ));
            return;
        }

        var openSequence = defineWindow.GetOpenClickSequence();
        if (openSequence.Length == 0)
        {
            failures.Add(new PreflightCalibrationFailure(
                Key: "define_notch_profiles_open_sequence_missing",
                Message: "缺少 Define notch profiles 入口点击序列",
                Expected: "define_notch_profiles.openClickSequence / openClickPoint configured",
                Actual: "null",
                Suggestion: "请重新标定 Define notch profiles 的入口点击点。"
            ));
        }
        else
        {
            for (var i = 0; i < openSequence.Length; i++)
            {
                if (!IsPointWithinWindow(openSequence[i], windowWidth, windowHeight))
                {
                    failures.Add(new PreflightCalibrationFailure(
                        Key: "define_notch_profiles_open_sequence_out_of_window",
                        Message: $"Define notch profiles 第 {i + 1} 个入口点击点不在窗口内",
                        Expected: $"0<=x<{windowWidth}, 0<=y<{windowHeight}",
                        Actual: $"({openSequence[i].X},{openSequence[i].Y})",
                        Suggestion: "请重新标定 Define notch profiles 入口点击点，确保点位位于主窗口内。"
                    ));
                }
            }
        }

        var listTarget = defineWindow.FindListTarget("notch_profiles_list");
        if (listTarget is null)
        {
            failures.Add(new PreflightCalibrationFailure(
                Key: "notch_profiles_list_target_missing",
                Message: "缺少 Notch Profiles 列表导航配置",
                Expected: "define_notch_profiles.listTargets.notch_profiles_list configured",
                Actual: "null",
                Suggestion: "请重新标定 Notch Profiles 列表 ROI、首行锚点、行高和 Edit 按钮。"
            ));
            return;
        }

        if (listTarget.RoiWindow is not BBox roi)
        {
            failures.Add(new PreflightCalibrationFailure(
                Key: "notch_profiles_list_roi_missing",
                Message: "缺少 Notch Profiles 列表 ROI",
                Expected: "notch_profiles_list.RoiWindow configured",
                Actual: "null",
                Suggestion: "请重新标定 Notch Profiles 列表 ROI。"
            ));
            return;
        }

        if (!IsRoiWithinWindow(roi, windowWidth, windowHeight))
        {
            failures.Add(new PreflightCalibrationFailure(
                Key: "notch_profiles_list_roi_out_of_window",
                Message: "Notch Profiles 列表 ROI 不在最大化子窗口范围内",
                Expected: $"roi within 0..{windowWidth}x{windowHeight}",
                Actual: $"({roi.X},{roi.Y},{roi.Width},{roi.Height})",
                Suggestion: "请重新标定 Notch Profiles 列表 ROI，确保其位于最大化子窗口范围内。"
            ));
        }

        if (listTarget.FirstRowAnchor is not WindowPoint firstRowAnchor)
        {
            failures.Add(new PreflightCalibrationFailure(
                Key: "notch_profiles_first_row_anchor_missing",
                Message: "缺少 Notch Profiles 首行锚点",
                Expected: "notch_profiles_list.FirstRowAnchor configured",
                Actual: "null",
                Suggestion: "请重新标定 Notch Profiles 首行锚点。"
            ));
        }
        else if (!IsPointWithinRoi(firstRowAnchor, roi))
        {
            failures.Add(new PreflightCalibrationFailure(
                Key: "notch_profiles_first_row_anchor_out_of_roi",
                Message: "Notch Profiles 首行锚点不在列表 ROI 内",
                Expected: $"point within roi ({roi.X},{roi.Y},{roi.Width},{roi.Height})",
                Actual: $"({firstRowAnchor.X},{firstRowAnchor.Y})",
                Suggestion: "请重新标定 Notch Profiles 首行锚点，确保位于列表 ROI 内。"
            ));
        }

        if (listTarget.RowHeight is not int rowHeight || rowHeight <= 0)
        {
            failures.Add(new PreflightCalibrationFailure(
                Key: "notch_profiles_row_height_invalid",
                Message: "缺少有效的 Notch Profiles 行高",
                Expected: "notch_profiles_list.RowHeight > 0",
                Actual: listTarget.RowHeight?.ToString() ?? "null",
                Suggestion: "请重新标定第 1/第 2 行锚点，确保行高被正确写入。"
            ));
        }
        else if (listTarget.FirstRowAnchor is WindowPoint anchor)
        {
            var secondRowPoint = new WindowPoint(anchor.X, anchor.Y + rowHeight);
            if (!IsPointWithinRoi(secondRowPoint, roi))
            {
                failures.Add(new PreflightCalibrationFailure(
                    Key: "notch_profiles_second_row_out_of_roi",
                    Message: "根据首行锚点与行高推导出的第 2 行点击点超出列表 ROI",
                    Expected: $"point within roi ({roi.X},{roi.Y},{roi.Width},{roi.Height})",
                    Actual: $"({secondRowPoint.X},{secondRowPoint.Y})",
                    Suggestion: "请重新标定 Notch Profiles 第 1/第 2 行锚点；若列表布局已漂移，请先恢复布局后再标定。"
                ));
            }
        }

        if (listTarget.ActionClickPoint is not WindowPoint actionClickPoint)
        {
            failures.Add(new PreflightCalibrationFailure(
                Key: "notch_profiles_action_click_missing",
                Message: "缺少 Notch Profiles 的 Edit 按钮点击点",
                Expected: "notch_profiles_list.ActionClickPoint configured",
                Actual: "null",
                Suggestion: "请重新标定 Notch Profiles 的 Edit 按钮点击点。"
            ));
        }
        else if (!IsPointWithinWindow(actionClickPoint, windowWidth, windowHeight))
        {
            failures.Add(new PreflightCalibrationFailure(
                Key: "notch_profiles_action_click_out_of_window",
                Message: "Notch Profiles 的 Edit 按钮点击点不在最大化子窗口范围内",
                Expected: $"0<=x<{windowWidth}, 0<=y<{windowHeight}",
                Actual: $"({actionClickPoint.X},{actionClickPoint.Y})",
                Suggestion: "请重新标定 Notch Profiles 的 Edit 按钮点击点。"
            ));
        }

        var layoutVerify = defineWindow.FindVerifyTarget("notch_profiles_layout");
        if (layoutVerify is null || layoutVerify.RoiWindow is null || string.IsNullOrWhiteSpace(layoutVerify.Sha256))
        {
            failures.Add(new PreflightCalibrationFailure(
                Key: "notch_profiles_layout_verify_missing",
                Message: "缺少 Notch Profiles 列表布局签名",
                Expected: "define_notch_profiles.VerifyTargets.notch_profiles_layout (RoiWindow + Sha256) configured",
                Actual: "null",
                Suggestion: "请执行 Notch Profiles 布局签名标定，固化列表首屏与 Edit 区域的标准布局。"
            ));
        }
        else
        {
            var verifyRoi = layoutVerify.RoiWindow.Value;
            if (!IsRoiWithinWindow(verifyRoi, windowWidth, windowHeight))
            {
                failures.Add(new PreflightCalibrationFailure(
                    Key: "notch_profiles_layout_verify_out_of_window",
                    Message: "Notch Profiles 列表布局签名 ROI 不在最大化子窗口范围内",
                    Expected: $"roi within 0..{windowWidth}x{windowHeight}",
                    Actual: $"({verifyRoi.X},{verifyRoi.Y},{verifyRoi.Width},{verifyRoi.Height})",
                    Suggestion: "请重新标定 Notch Profiles 布局签名，确保签名 ROI 位于最大化子窗口范围内。"
                ));
            }
        }
    }

    private static bool IsPointWithinWindow(WindowPoint point, int windowWidth, int windowHeight)
        => point.X >= 0 &&
           point.Y >= 0 &&
           point.X < windowWidth &&
           point.Y < windowHeight;

    private static bool IsPointWithinRoi(WindowPoint point, BBox roi)
        => point.X >= roi.X &&
           point.Y >= roi.Y &&
           point.X < (roi.X + roi.Width) &&
           point.Y < (roi.Y + roi.Height);

    private static bool IsRoiWithinWindow(BBox roi, int windowWidth, int windowHeight)
        => roi.X >= 0 &&
           roi.Y >= 0 &&
           roi.Width > 0 &&
           roi.Height > 0 &&
           (roi.X + roi.Width) <= windowWidth &&
           (roi.Y + roi.Height) <= windowHeight;
}
