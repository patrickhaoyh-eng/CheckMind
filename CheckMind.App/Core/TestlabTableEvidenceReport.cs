using System.Text.Json;

namespace CheckMind.App.Core;

public sealed record TestlabTableEvidenceReport(
    string TabName,
    string TableName,
    int ChunkCount,
    int UniqueChunkCount,
    int ChangedEventCount,
    int TerminalNoChangeEventCount,
    int ExpectedUniqueChunkCount,
    string DedupKey,
    bool IsConsistent,
    string? StitchedScreenshotPath,
    IReadOnlyList<string> UniqueChunkScreenshotPaths
)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions.Default);
}
