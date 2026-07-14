namespace CheckMind.App.Core;

public sealed record OcrBlock(string Text, BBox BBox, double? Confidence = null);

public sealed record OcrResult(string Engine, IReadOnlyList<OcrBlock> Blocks);

public sealed record OcrRequest(byte[] ImageBytes, string ImageMime, BBox Roi, string? Hint = null);

