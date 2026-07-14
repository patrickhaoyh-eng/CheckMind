using System.Text.Json.Serialization;

namespace CheckMind.App.Core;

public sealed record RunMeta(
    string RunId,
    DateTimeOffset CreatedAtUtc,
    string? TrialId = null,
    string? OperatorName = null,
    IReadOnlyList<RunInputRef>? Inputs = null
);

public sealed record RunInputRef(
    string OriginalPath,
    string StoredPath,
    string Kind,
    DateTimeOffset AddedAtUtc
);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RunMeta))]
internal partial class RunMetaJsonContext : JsonSerializerContext;

