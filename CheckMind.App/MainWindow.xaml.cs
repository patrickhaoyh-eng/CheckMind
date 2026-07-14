using System.IO;
using System.Windows;
using CheckMind.App.Core;

namespace CheckMind.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly RunStorage _runStorage = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void CreateRun_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var run = _runStorage.CreateRun();
            OutputTextBox.Text =
                $"RunId: {run.RunId}{Environment.NewLine}" +
                $"RunDirectory: {run.RunDirectory}{Environment.NewLine}" +
                $"Meta: {run.MetaPath}{Environment.NewLine}" +
                $"Results: {run.ResultsPath}{Environment.NewLine}";
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = ex.ToString();
        }
    }

    private async void RecalibrateVerifySignature_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureRepoDefaults();
            Environment.SetEnvironmentVariable("CHECKMIND_CALIBRATE_REUSE_EXISTING", "1");

            var tabs = GetTabsFromEnvOrDefault();
            var run = _runStorage.CreateRun();
            await new TestlabTabClickPointCalibrator().CalibrateAsync(run, tabs);

            OutputTextBox.Text =
                $"Recalibrate verify signature done.{Environment.NewLine}" +
                $"RunId: {run.RunId}{Environment.NewLine}" +
                $"RunDirectory: {run.RunDirectory}{Environment.NewLine}" +
                $"Profile: {Environment.GetEnvironmentVariable("CHECKMIND_WORKSTATION_PROFILE_PATH") ?? WorkstationProfileStore.CreateDefault().ProfilePath}{Environment.NewLine}";
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = ex.ToString();
        }
    }

    private async void CalibrateCaptureRoi_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureRepoDefaults();

            var tabs = GetTabsFromEnvOrDefault();
            var run = _runStorage.CreateRun();
            await new TestlabCaptureRoiCalibrator().CalibrateAsync(run, tabs);

            OutputTextBox.Text =
                $"Capture ROI calibrate done.{Environment.NewLine}" +
                $"RunId: {run.RunId}{Environment.NewLine}" +
                $"RunDirectory: {run.RunDirectory}{Environment.NewLine}" +
                $"Profile: {Environment.GetEnvironmentVariable("CHECKMIND_WORKSTATION_PROFILE_PATH") ?? WorkstationProfileStore.CreateDefault().ProfilePath}{Environment.NewLine}";
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = ex.ToString();
        }
    }

    private static string[] GetTabsFromEnvOrDefault()
    {
        var raw = Environment.GetEnvironmentVariable("CHECKMIND_TESTLAB_TABS");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var tabs = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tabs.Length > 0)
            {
                return tabs;
            }
        }

        return ["Channel Setup", "Sine Setup"];
    }

    private static void EnsureRepoDefaults()
    {
        var runsRoot = Environment.GetEnvironmentVariable("CHECKMIND_RUNS_ROOT");
        var profilePath = Environment.GetEnvironmentVariable("CHECKMIND_WORKSTATION_PROFILE_PATH");
        if (!string.IsNullOrWhiteSpace(runsRoot) && !string.IsNullOrWhiteSpace(profilePath))
        {
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            var sln = Path.Combine(dir.FullName, "CheckMind.sln");
            if (File.Exists(sln))
            {
                var repoRuns = Path.Combine(dir.FullName, "artifacts", "probe-runs");
                if (string.IsNullOrWhiteSpace(runsRoot) && Directory.Exists(repoRuns))
                {
                    Environment.SetEnvironmentVariable("CHECKMIND_RUNS_ROOT", repoRuns);
                }

                var repoProfile = Path.Combine(repoRuns, "_config", "workstation_profile.json");
                if (string.IsNullOrWhiteSpace(profilePath))
                {
                    Environment.SetEnvironmentVariable("CHECKMIND_WORKSTATION_PROFILE_PATH", repoProfile);
                }

                return;
            }

            dir = dir.Parent;
        }
    }
}
