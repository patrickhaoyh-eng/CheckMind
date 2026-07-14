using System.Text.Json.Serialization;

namespace CheckMind.App.Core;

public sealed record ExtractedField(
    string Key,
    string? Value,
    EvidenceRef Evidence
);

public sealed record ExtractedDocument(
    string DocKey,
    string SourceInputPath,
    IReadOnlyList<ExtractedField> Fields
);

public sealed record ExtractedInputs(
    string RunId,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ExtractedDocument> Documents
);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ExtractedInputs))]
internal partial class ExtractedInputsJsonContext : JsonSerializerContext;

