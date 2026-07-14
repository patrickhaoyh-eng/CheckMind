using System.IO;
using System.Text.RegularExpressions;

namespace CheckMind.App.Core;

public sealed class Form0146_06Extractor
{
    private static readonly Regex RowRegex = new(
        @"(?<point>(?:C|A)\s*\d{1,2})\s+(?<cable>(?:c|C)?\s*\d{1,3}|(?:g|G)?\s*\d{1,3}|LNS|lns)",
        RegexOptions.Compiled
    );

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

        var tableRoi = ImageGeometry.FromRelative(0.12, 0.18, 0.76, 0.70, w, h);

        var runner = new OcrRunner(ocr, new OcrStore());
        var ocrId = OcrId.Make("0146-06", Path.GetFileNameWithoutExtension(storedImagePath));
        var (_, ocrResult) = await runner.RunAsync(run, ocrId, bytes, mime, tableRoi, "0146-06 table", ct);

        var raw = ocrResult.Blocks.FirstOrDefault()?.Text?.Trim();

        var fields = new List<ExtractedField>
        {
            new ExtractedField(
                Key: "0146-06.table_text_raw",
                Value: string.IsNullOrWhiteSpace(raw) ? null : raw,
                Evidence: new EvidenceRef(storedImagePath, tableRoi, PageKey: "0146-06", FieldKey: "table_text_raw", Confidence: ocrResult.Blocks.FirstOrDefault()?.Confidence)
            )
        };

        if (!string.IsNullOrWhiteSpace(raw))
        {
            var index = 0;
            foreach (Match m in RowRegex.Matches(raw))
            {
                var point = NormalizeCompact(m.Groups["point"].Value);
                var cable = NormalizeCompact(m.Groups["cable"].Value);

                fields.Add(
                    new ExtractedField(
                        Key: $"0146-06.rows[{index}].point_id",
                        Value: point,
                        Evidence: new EvidenceRef(storedImagePath, tableRoi, PageKey: "0146-06", FieldKey: "rows.point_id", Confidence: null)
                    )
                );
                fields.Add(
                    new ExtractedField(
                        Key: $"0146-06.rows[{index}].sensor_cable_no",
                        Value: cable,
                        Evidence: new EvidenceRef(storedImagePath, tableRoi, PageKey: "0146-06", FieldKey: "rows.sensor_cable_no", Confidence: null)
                    )
                );

                index++;
            }
        }

        return new ExtractedDocument("0146-06", storedImagePath, fields);
    }

    private static string NormalizeCompact(string s)
    {
        return new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }
}

