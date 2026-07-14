using System.Text.Json;

namespace CheckMind.App.Core;

public sealed record TestlabTableScrollEvent(
    int Step,
    string BeforeSerialSha256,
    string? AfterSerialSha256,
    string BeforeScrollbarSha256,
    string? AfterScrollbarSha256,
    string Method,
    int PauseMs,
    bool Changed
);

public sealed record TestlabTableScrollEventsReport(
    string TabName,
    string TableName,
    BBox TableRoiScreen,
    BBox SerialRoiScreen,
    BBox ScrollbarRoiScreen,
    IReadOnlyList<TestlabTableScrollEvent> Events
)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions.Default);
}
