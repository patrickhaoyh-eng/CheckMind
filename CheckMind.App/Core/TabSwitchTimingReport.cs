using System.Text.Json;

namespace CheckMind.App.Core;

public sealed record TabSwitchTimingReport(
    string TabName,
    string ClickSource,
    bool FastEnabled,
    bool Verified,
    string VerifyMode,
    WindowPoint? ClickPointWindow,
    int? ClickScreenX,
    int? ClickScreenY,
    string? BeforeWindowPath,
    string? AfterWindowPath,
    long TotalMs,
    long CaptureWindowMs,
    long VerifyMs,
    int ClickAttempts,
    int AttemptXCount,
    int AttemptYCount,
    int MaxClickTries
)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions.Default);
}
