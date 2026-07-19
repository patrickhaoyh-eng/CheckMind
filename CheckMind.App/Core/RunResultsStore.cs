using System.Text;
using System.Text.Json;
using System.IO;

namespace CheckMind.App.Core;

public sealed class RunResultsStore
{
    public RunResults Load(string resultsPath)
    {
        var json = File.ReadAllText(resultsPath, Encoding.UTF8);
        var result = JsonSerializer.Deserialize(json, RunResultsJsonContext.Default.RunResults);
        return result ?? throw new InvalidOperationException("Unable to parse results.json");
    }

    public void Save(string resultsPath, RunResults results)
    {
        var json = JsonSerializer.Serialize(results, RunResultsJsonContext.Default.RunResults);
        File.WriteAllText(resultsPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void Initialize(RunContext run, TaskCreateRequest? taskRequest)
    {
        var results = new RunResults(
            RunId: run.RunId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            TaskRequest: taskRequest,
            TaskResult: null,
            Alerts: Array.Empty<TaskAlert>()
        );

        Save(run.ResultsPath, results);
    }

    public void SaveTestlabResult(RunContext run, TaskCreateRequest? taskRequest, TestlabRunResult testlabResult)
    {
        var alerts = new List<TaskAlert>();
        TaskExecutionResult taskResult;

        if (testlabResult.NotchProfileCountMismatch is { } mismatch)
        {
            alerts.Add(new TaskAlert(
                Code: CheckMindTaskResultCodes.NotchProfileCountMismatch,
                Severity: "warning",
                Message: mismatch.UserMessage
            ));

            taskResult = new TaskExecutionResult(
                Status: "completed_with_warning",
                CompletedNotchProfileCount: mismatch.CompletedCount,
                RequestedNotchProfileCount: mismatch.RequestedCount,
                ResultCode: CheckMindTaskResultCodes.NotchProfileCountMismatch,
                Message: mismatch.UserMessage,
                FailedRow: mismatch.FailedRowIndex
            );
        }
        else
        {
            taskResult = new TaskExecutionResult(
                Status: "completed",
                CompletedNotchProfileCount: testlabResult.NotchProfileScans?.Count,
                RequestedNotchProfileCount: taskRequest?.NotchProfileCount
            );
        }

        var results = new RunResults(
            RunId: run.RunId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            TaskRequest: taskRequest,
            TaskResult: taskResult,
            Alerts: alerts
        );

        Save(run.ResultsPath, results);
    }
}
