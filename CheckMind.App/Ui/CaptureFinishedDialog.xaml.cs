using System.Windows;
using CheckMind.App.Core;

namespace CheckMind.App.Ui;

public partial class CaptureFinishedDialog : Window
{
    public CaptureFinishedDialog(string? runDirectory = null)
    {
        InitializeComponent();
        RunDirectory = string.IsNullOrWhiteSpace(runDirectory) ? null : runDirectory;
        var summary = CaptureFinishedSummaryBuilder.BuildFromRunDirectory(RunDirectory);
        HeadlineText.Text = summary.Headline;
        DetailText.Text = summary.Detail ?? "";
        DetailText.Visibility = string.IsNullOrWhiteSpace(summary.Detail) ? Visibility.Collapsed : Visibility.Visible;
        RunDirectoryText.Text = string.IsNullOrWhiteSpace(RunDirectory)
            ? "保存路径：本次未记录到 run 目录。"
            : $"保存路径：{RunDirectory}";
        OpenRunButton.Click += (_, _) => { OpenRunDirectory = true; Close(); };
        OkButton.Click += (_, _) => { OpenRunDirectory = false; Close(); };
    }

    public bool OpenRunDirectory { get; private set; }

    public string? RunDirectory { get; }
}
