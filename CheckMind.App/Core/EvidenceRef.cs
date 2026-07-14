namespace CheckMind.App.Core;

public readonly record struct BBox(int X, int Y, int Width, int Height);

public sealed record EvidenceRef(
    string ImagePath,
    BBox BBox,
    string? PageKey = null,
    string? FieldKey = null,
    double? Confidence = null
);

