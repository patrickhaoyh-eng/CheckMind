using System.Text;
using System.Collections.Generic;
using System.IO;

namespace CheckMind.App.Core;

public sealed class WorkstationComplianceChecker
{
    private const int BaselineWidth = 1920;
    private const int BaselineHeight = 1080;
    private const int BaselineDpiScalePercent = 100;

    private readonly WorkstationProfileStore _store;
    private readonly WorkstationEnvironmentProbe _probe;

    public WorkstationComplianceChecker(WorkstationProfileStore store, WorkstationEnvironmentProbe probe)
    {
        _store = store;
        _probe = probe;
    }

    public static WorkstationComplianceChecker CreateDefault()
        => new WorkstationComplianceChecker(WorkstationProfileStore.CreateDefault(), new WorkstationEnvironmentProbe());

    public WorkstationComplianceReport Check(RunContext run, TestlabWindowInfo win, WindowController controller)
    {
        var measured = _probe.Probe(win.Hwnd);

        WorkstationProfile? profile = null;
        var failures = new List<WorkstationComplianceFailure>();

        if (!File.Exists(_store.ProfilePath))
        {
            var template = CreateTemplate(measured);
            var templatePath = Path.Combine(run.RunDirectory, "workstation_profile_template.json");
            File.WriteAllText(templatePath, template, Encoding.UTF8);

            failures.Add(new WorkstationComplianceFailure(
                Key: "profile_missing",
                Message: "未找到固定工位 profile 配置文件。",
                Expected: _store.ProfilePath,
                Actual: null,
                Suggestion: $"请将 {templatePath} 复制到 {_store.ProfilePath}，或通过 CHECKMIND_WORKSTATION_PROFILE_PATH 指定 profile 路径。"
            ));

            return new WorkstationComplianceReport(
                IsCompliant: false,
                ProfilePath: _store.ProfilePath,
                Expected: null,
                Measured: measured,
                FailedChecks: failures
            );
        }

        profile = _store.Load();

        if (profile.Environment.TargetMonitorIndex > 0 &&
            measured.WindowMonitorIndex != profile.Environment.TargetMonitorIndex)
        {
            failures.Add(new WorkstationComplianceFailure(
                Key: "target_monitor",
                Message: "Testlab 当前不在指定的目标显示器上。",
                Expected: $"显示器 {profile.Environment.TargetMonitorIndex}",
                Actual: $"显示器 {measured.WindowMonitorIndex}",
                Suggestion: "请将 Testlab 移动到规定的目标显示器并保持该屏为抓取屏后重试。"
            ));
        }

        if (measured.WindowMonitorWidth != profile.Environment.TargetWidth || measured.WindowMonitorHeight != profile.Environment.TargetHeight)
        {
            failures.Add(new WorkstationComplianceFailure(
                Key: "target_monitor_resolution",
                Message: "目标显示器分辨率不符合固定工位要求。",
                Expected: $"{profile.Environment.TargetWidth}x{profile.Environment.TargetHeight}",
                Actual: $"{measured.WindowMonitorWidth}x{measured.WindowMonitorHeight}",
                Suggestion: "请将用于运行 Testlab 的目标显示器分辨率切换到工位标准值后重试。"
            ));
        }

        if (measured.DpiScalePercent != profile.Environment.DpiScalePercent)
        {
            failures.Add(new WorkstationComplianceFailure(
                Key: "dpi_scale",
                Message: "Windows 缩放比例不符合固定工位要求。",
                Expected: $"{profile.Environment.DpiScalePercent}%",
                Actual: $"{measured.DpiScalePercent}%",
                Suggestion: "请将 Windows 显示缩放调整为标准值并重新登录后重试。"
            ));
        }

        if (profile.Window.MustBeMaximized && !controller.IsMaximized(win.Hwnd))
        {
            failures.Add(new WorkstationComplianceFailure(
                Key: "window_maximized",
                Message: "Testlab 窗口未处于最大化状态。",
                Expected: "Maximized",
                Actual: "NotMaximized",
                Suggestion: "请先最大化 Testlab 主窗口并保持前台，然后重新执行。"
            ));
        }

        var tol = Math.Max(0, profile.Tolerances.PixelTolerance);
        var expectedRect = profile.Window.WindowRectScreen;
        var monitorBounds = TryGetWindowMonitorBounds(measured);
        var measuredRelativeRect = new BBox(
            measured.WindowRectScreen.X - monitorBounds.X,
            measured.WindowRectScreen.Y - monitorBounds.Y,
            measured.WindowRectScreen.Width,
            measured.WindowRectScreen.Height
        );

        var windowRectTol = measured.IsMaximized
            ? Math.Max(tol, 32)
            : tol;
        var nearAbs = IsRectNear(measured.WindowRectScreen, expectedRect, windowRectTol);
        var nearRel = IsRectNear(measuredRelativeRect, expectedRect, windowRectTol);

        if (!nearAbs && !nearRel)
        {
            failures.Add(new WorkstationComplianceFailure(
                Key: "window_rect",
                Message: "Testlab 窗口位置或尺寸与固定工位 profile 不一致。",
                Expected: expectedRect.ToString(),
                Actual: $"abs={measured.WindowRectScreen};rel={measuredRelativeRect};monitor={monitorBounds}",
                Suggestion: "请将 Testlab 恢复到标准布局并最大化（必要时关闭/复位停靠面板），确保窗口完整位于目标显示器的标准位置。"
            ));
        }

        return new WorkstationComplianceReport(
            IsCompliant: failures.Count == 0,
            ProfilePath: _store.ProfilePath,
            Expected: profile,
            Measured: measured,
            FailedChecks: failures
        );
    }

    private static bool IsRectNear(BBox a, BBox b, int tol)
        => Math.Abs(a.X - b.X) <= tol &&
           Math.Abs(a.Y - b.Y) <= tol &&
           Math.Abs(a.Width - b.Width) <= tol &&
           Math.Abs(a.Height - b.Height) <= tol;

    private static BBox TryGetWindowMonitorBounds(WorkstationMeasuredEnvironment measured)
    {
        foreach (var m in measured.Win32Monitors)
        {
            if (m.Index == measured.WindowMonitorIndex)
            {
                return m.Bounds;
            }
        }

        foreach (var m in measured.WinFormsMonitors)
        {
            if (m.Index == measured.WindowMonitorIndex)
            {
                return m.Bounds;
            }
        }

        return new BBox(0, 0, measured.WindowMonitorWidth, measured.WindowMonitorHeight);
    }

    private static string CreateTemplate(WorkstationMeasuredEnvironment measured)
    {
        var profile = new WorkstationProfile(
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
                TabClickPoints:
                [
                    new WorkstationTabClickTarget("Channel Setup"),
                    new WorkstationTabClickTarget("Sine Setup")
                ]
            ),
            [],
            [
                CreatePageProfileTemplate("Channel Setup"),
                CreatePageProfileTemplate("Sine Setup")
            ]
        );

        return JsonOptions.Default.WriteIndented
            ? System.Text.Json.JsonSerializer.Serialize(profile, JsonOptions.Default)
            : System.Text.Json.JsonSerializer.Serialize(profile);
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
}
