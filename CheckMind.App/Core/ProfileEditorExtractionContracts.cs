using System.Text.Json.Serialization;

namespace CheckMind.App.Core;

public static class ProfileEditorExtractionMappingStatuses
{
    public const string Unmapped = "unmapped";
    public const string PartiallyMapped = "partially_mapped";
    public const string Mapped = "mapped";
}

public static class ProfileEditorExtractionReviewStatuses
{
    public const string PendingReview = "pending_review";
    public const string Reviewed = "reviewed";
    public const string NeedsAttention = "needs_attention";
}

public static class ProfileEditorExtractionSourceTypes
{
    public const string Stitched = "stitched";
    public const string Chunk = "chunk";
    public const string Crop = "crop";
}

public sealed record ProfileEditorExtractionResult(
    [property: JsonPropertyName("objectKey")] string ObjectKey,
    [property: JsonPropertyName("sourceRunId")] string SourceRunId,
    [property: JsonPropertyName("sourceTaskType")] string SourceTaskType,
    [property: JsonPropertyName("sourceScreenshotPath")] string SourceScreenshotPath,
    [property: JsonPropertyName("mappingStatus")] string MappingStatus,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("rows")] IReadOnlyList<ProfileEditorExtractedRow> Rows
);

public sealed record ProfileEditorExtractedRow(
    [property: JsonPropertyName("rowIndex")] int RowIndex,
    [property: JsonPropertyName("rowKey")] string RowKey,
    [property: JsonPropertyName("rowEvidenceRef")] ProfileEditorEvidenceRef RowEvidenceRef,
    [property: JsonPropertyName("cells")] IReadOnlyList<ProfileEditorExtractedCell> Cells
);

public sealed record ProfileEditorExtractedCell(
    [property: JsonPropertyName("columnIndex")] int ColumnIndex,
    [property: JsonPropertyName("fieldKey")] string FieldKey,
    [property: JsonPropertyName("fieldLabelRaw")] string? FieldLabelRaw,
    [property: JsonPropertyName("rawText")] string? RawText,
    [property: JsonPropertyName("normalizedValue")] string? NormalizedValue,
    [property: JsonPropertyName("rawUnit")] string? RawUnit,
    [property: JsonPropertyName("normalizedUnit")] string? NormalizedUnit,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("evidenceRef")] ProfileEditorEvidenceRef EvidenceRef,
    [property: JsonPropertyName("normalizationNotes")] IReadOnlyList<string>? NormalizationNotes = null
);

public sealed record ProfileEditorEvidenceRef(
    [property: JsonPropertyName("screenshotPath")] string ScreenshotPath,
    [property: JsonPropertyName("bbox")] ProfileEditorBBox BBox,
    [property: JsonPropertyName("rowIndex")] int? RowIndex = null,
    [property: JsonPropertyName("columnIndex")] int? ColumnIndex = null,
    [property: JsonPropertyName("sourceType")] string SourceType = ProfileEditorExtractionSourceTypes.Stitched
);

[JsonSerializable(typeof(ProfileEditorBBox))]
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ProfileEditorExtractionResult))]
[JsonSerializable(typeof(ProfileEditorExtractedRow))]
[JsonSerializable(typeof(ProfileEditorExtractedCell))]
[JsonSerializable(typeof(ProfileEditorEvidenceRef))]
internal partial class ProfileEditorExtractionJsonContext : JsonSerializerContext;

public sealed record ProfileEditorBBox(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height
);
