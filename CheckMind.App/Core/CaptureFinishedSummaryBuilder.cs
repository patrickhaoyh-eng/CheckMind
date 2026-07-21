using System.IO;
using System.Text;

namespace CheckMind.App.Core;

public sealed record CaptureFinishedSummary(
    string Headline,
    string? Detail = null
);

public static class CaptureFinishedSummaryBuilder
{
    public static CaptureFinishedSummary BuildFromRunDirectory(string? runDirectory)
    {
        if (string.IsNullOrWhiteSpace(runDirectory))
        {
            return new CaptureFinishedSummary("抓取结束，已归还鼠标控制。");
        }

        var resultsPath = Path.Combine(runDirectory, "results.json");
        if (!File.Exists(resultsPath))
        {
            return new CaptureFinishedSummary("抓取结束，已归还鼠标控制。");
        }

        try
        {
            var results = new RunResultsStore().Load(resultsPath);
            return BuildFromResults(results);
        }
        catch
        {
            return new CaptureFinishedSummary("抓取结束，已归还鼠标控制。");
        }
    }

    public static CaptureFinishedSummary BuildFromResults(RunResults results)
    {
        var headline = "抓取结束，已归还鼠标控制。";
        var details = new List<string>();

        var taskResult = results.TaskResult;
        if (taskResult is not null)
        {
            if (string.Equals(taskResult.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                details.Add("正式核验：全部通过。");
            }
            else if (string.Equals(taskResult.Status, "completed_with_warning", StringComparison.OrdinalIgnoreCase))
            {
                details.Add("运行已完成，但存在需关注项。");
            }
        }

        var summary = results.FormalVerificationSummary;
        if (summary is not null)
        {
            if (summary.AllVerified)
            {
                if (!details.Contains("正式核验：全部通过。"))
                {
                    details.Add("正式核验：全部通过。");
                }
            }
            else
            {
                details.Add("正式核验未全部通过：");
                foreach (var reason in summary.FailedReasons ?? Array.Empty<TaskFormalFailureReason>())
                {
                    foreach (var message in reason.ReasonMessages ?? Array.Empty<string>())
                    {
                        details.Add($"- {message}");
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(taskResult?.Message))
        {
            details.Add($"结果提示：{taskResult.Message}");
        }

        if (results.Alerts is { Count: > 0 })
        {
            foreach (var alert in results.Alerts)
            {
                if (!string.IsNullOrWhiteSpace(alert.Message))
                {
                    details.Add($"告警：{alert.Message}");
                }
            }
        }

        return new CaptureFinishedSummary(
            headline,
            details.Count == 0 ? null : string.Join(Environment.NewLine, details)
        );
    }
}
