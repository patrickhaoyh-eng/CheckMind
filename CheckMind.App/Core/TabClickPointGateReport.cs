using System.Text.Json;

namespace CheckMind.App.Core;

public sealed record TabClickPointGateReport(
    string TabName,
    string RuleKey,
    string Message,
    WindowPoint? ClickPointWindow,
    int WindowWidth,
    int WindowHeight,
    string? BeforeWindowPath,
    string? AfterWindowPath,
    string? BeforeTabsRoiPath,
    string? AfterTabsRoiPath,
    string? SuggestedOcrPath,
    WindowPoint? SuggestedClickPointWindow,
    long TotalMs
)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions.Default);
}

