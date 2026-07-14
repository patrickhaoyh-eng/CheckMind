using System.Text.Json;

namespace CheckMind.App.Core;

public sealed record WorkstationComplianceFailure(
    string Key,
    string Message,
    string? Expected,
    string? Actual,
    string? Suggestion
);

public sealed record WorkstationComplianceReport(
    bool IsCompliant,
    string? ProfilePath,
    WorkstationProfile? Expected,
    WorkstationMeasuredEnvironment Measured,
    IReadOnlyList<WorkstationComplianceFailure> FailedChecks
)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions.Default);
}
