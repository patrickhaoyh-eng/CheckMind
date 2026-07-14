using System.Text.Json;

namespace CheckMind.App.Core;

public sealed record PreflightCalibrationFailure(
    string Key,
    string Message,
    string? Expected = null,
    string? Actual = null,
    string? Suggestion = null
);

public sealed record PreflightCalibrationReport(
    bool IsCompliant,
    string ProfilePath,
    string[] Tabs,
    PreflightCalibrationFailure[] Failures,
    PreflightCalibrationFailure[]? Warnings = null
)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions.Default);
}
