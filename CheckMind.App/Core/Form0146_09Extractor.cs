using System.IO;
using System.Text.RegularExpressions;

namespace CheckMind.App.Core;

public sealed class Form0146_09Extractor
{
    private static readonly Regex PageIdRegex = new(@"0146-09-\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<ExtractedDocument> ExtractAsync(
        RunContext run,
        string storedImagePath,
        IOcrAdapter ocr,
        CancellationToken ct = default
    )
    {
        var bytes = File.ReadAllBytes(storedImagePath);
        var mime = OcrRunner.InferMimeFromPath(storedImagePath);
        var (w, h) = ImageGeometry.GetSize(bytes);

        var runner = new OcrRunner(ocr, new OcrStore());

        var headerRoi = ImageGeometry.FromRelative(0.66, 0.02, 0.32, 0.18, w, h);
        var headerOcrId = OcrId.Make("0146-09_header", Path.GetFileNameWithoutExtension(storedImagePath));
        var (_, headerOcr) = await runner.RunAsync(run, headerOcrId, bytes, mime, headerRoi, "0146-09 header", ct);
        var headerText = headerOcr.Blocks.FirstOrDefault()?.Text ?? "";
        var pageIdMatch = PageIdRegex.Match(headerText);
        var pageId = pageIdMatch.Success ? pageIdMatch.Value.ToUpperInvariant() : "0146-09";

        var fields = new List<ExtractedField>
        {
            new ExtractedField(
                Key: "0146-09.page_id",
                Value: pageId,
                Evidence: new EvidenceRef(storedImagePath, headerRoi, PageKey: pageId, FieldKey: "page_id", Confidence: headerOcr.Blocks.FirstOrDefault()?.Confidence)
            )
        };

        foreach (var axis in new[] { "x", "y", "z" })
        {
            var (freqRoi, levelRoi) = GetAxisRois(axis, w, h);

            var freqOcrId = OcrId.Make($"{pageId}_{axis}_freq", Path.GetFileNameWithoutExtension(storedImagePath));
            var levelOcrId = OcrId.Make($"{pageId}_{axis}_level", Path.GetFileNameWithoutExtension(storedImagePath));

            var (_, freqOcr) = await runner.RunAsync(run, freqOcrId, bytes, mime, freqRoi, $"{pageId} {axis} freq", ct);
            var (_, levelOcr) = await runner.RunAsync(run, levelOcrId, bytes, mime, levelRoi, $"{pageId} {axis} level", ct);

            var freqText = NormalizeLines(freqOcr.Blocks.FirstOrDefault()?.Text);
            var levelText = NormalizeLines(levelOcr.Blocks.FirstOrDefault()?.Text);

            fields.Add(
                new ExtractedField(
                    Key: $"0146-09.{axis}.freq_text",
                    Value: string.IsNullOrWhiteSpace(freqText) ? null : freqText,
                    Evidence: new EvidenceRef(storedImagePath, freqRoi, PageKey: pageId, FieldKey: $"{axis}.freq_text", Confidence: freqOcr.Blocks.FirstOrDefault()?.Confidence)
                )
            );
            fields.Add(
                new ExtractedField(
                    Key: $"0146-09.{axis}.level_text",
                    Value: string.IsNullOrWhiteSpace(levelText) ? null : levelText,
                    Evidence: new EvidenceRef(storedImagePath, levelRoi, PageKey: pageId, FieldKey: $"{axis}.level_text", Confidence: levelOcr.Blocks.FirstOrDefault()?.Confidence)
                )
            );
        }

        return new ExtractedDocument(pageId, storedImagePath, fields);
    }

    private static (BBox Freq, BBox Level) GetAxisRois(string axis, int imageWidth, int imageHeight)
    {
        var y = 0.26;
        var height = 0.36;

        (double x, double w) = axis switch
        {
            "x" => (0.06, 0.26),
            "y" => (0.37, 0.25),
            "z" => (0.67, 0.25),
            _ => (0.06, 0.26),
        };

        var freq = ImageGeometry.FromRelative(x + 0.00, y, w * 0.52, height, imageWidth, imageHeight);
        var level = ImageGeometry.FromRelative(x + w * 0.52, y, w * 0.48, height, imageWidth, imageHeight);
        return (freq, level);
    }

    private static string? NormalizeLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        return lines.Length == 0 ? null : string.Join("\n", lines);
    }
}

