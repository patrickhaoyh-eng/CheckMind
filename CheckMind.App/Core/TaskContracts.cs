using System.Text.Json.Serialization;

namespace CheckMind.App.Core;

public static class CheckMindTaskTypes
{
    public const string Sine = "sine";
    public const string Random = "random";
}

public static class CheckMindTaskResultCodes
{
    public const string NotchProfileCountMismatch = "NOTCH_PROFILE_COUNT_MISMATCH";
}

public sealed record TaskCreateRequest(
    [property: JsonPropertyName("taskType")] string TaskType,
    [property: JsonPropertyName("notchProfileCount")] int? NotchProfileCount = null
);

public sealed record TaskExecutionResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("completedNotchProfileCount")] int? CompletedNotchProfileCount = null,
    [property: JsonPropertyName("requestedNotchProfileCount")] int? RequestedNotchProfileCount = null,
    [property: JsonPropertyName("resultCode")] string? ResultCode = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("failedRow")] int? FailedRow = null
);

public sealed record TaskAlert(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("message")] string Message
);

public sealed record RunResults(
    [property: JsonPropertyName("runId")] string RunId,
    [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("taskRequest")] TaskCreateRequest? TaskRequest = null,
    [property: JsonPropertyName("taskResult")] TaskExecutionResult? TaskResult = null,
    [property: JsonPropertyName("alerts")] IReadOnlyList<TaskAlert>? Alerts = null
);

public static class TaskContractResolver
{
    public static TaskCreateRequest? ResolveFromEnvironment()
    {
        var notchProfileCount = NotchProfileIndexResolver.ResolveCountFromEnvironment();
        if (notchProfileCount.HasValue)
        {
            return new TaskCreateRequest(
                TaskType: CheckMindTaskTypes.Sine,
                NotchProfileCount: notchProfileCount.Value
            );
        }

        var rawIndexes = (Environment.GetEnvironmentVariable(NotchProfileIndexResolver.IndexesEnvName) ?? string.Empty).Trim();
        var rawIndex = (Environment.GetEnvironmentVariable(NotchProfileIndexResolver.IndexEnvName) ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(rawIndexes) || !string.IsNullOrWhiteSpace(rawIndex))
        {
            return new TaskCreateRequest(
                TaskType: CheckMindTaskTypes.Sine
            );
        }

        return null;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TaskCreateRequest))]
[JsonSerializable(typeof(TaskExecutionResult))]
[JsonSerializable(typeof(TaskAlert))]
[JsonSerializable(typeof(RunResults))]
internal partial class RunResultsJsonContext : JsonSerializerContext;
