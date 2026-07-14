using System.IO;

namespace CheckMind.App.Core;

public sealed class SampleExtractionRunner
{
    public async Task<string> RunAsync(RunContext run, string sampleDirectory, CancellationToken ct = default)
    {
        var cfg = new AppConfigStore().LoadOrCreateDefault();
        var only = Environment.GetEnvironmentVariable("CHECKMIND_SAMPLE_ONLY");
        var probeImage = Directory.EnumerateFiles(sampleDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => IsSupportedImage(path))
            .Where(path => MatchesOnly(path, only))
            .FirstOrDefault();
        var selection = await new OcrModelSelector().SelectAsync(cfg, probeImage, run, ct);
        var ocr = selection.Adapter;

        var inputManager = new RunInputManager();
        var metaStore = new RunMetaStore();

        var docs = new List<ExtractedDocument>();

        foreach (var file in Directory.EnumerateFiles(sampleDirectory, "*.*", SearchOption.TopDirectoryOnly))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            {
                continue;
            }

            if (!MatchesOnly(file, only))
            {
                continue;
            }

            var inputRef = inputManager.AddFileToRunInputs(run, file, "sample");
            metaStore.AppendInput(run, inputRef);

            var stored = inputRef.StoredPath;
            var docKey = ClassifyByFileNameIfEnabled(file, only);
            docKey ??= await ClassifyAsync(run, stored, ocr, ct);

            if (string.Equals(docKey, "0146-06", StringComparison.OrdinalIgnoreCase))
            {
                docs.Add(await new Form0146_06Extractor().ExtractAsync(run, stored, ocr, ct));
            }
            else if (string.Equals(docKey, "0146-09", StringComparison.OrdinalIgnoreCase))
            {
                docs.Add(await new Form0146_09Extractor().ExtractAsync(run, stored, ocr, ct));
            }
        }

        docs.Add(
            new ExtractedDocument(
                "ocr_selection",
                probeImage ?? sampleDirectory,
                new[]
                {
                    new ExtractedField(
                        "ocr.selection",
                        selection.Description,
                        new EvidenceRef(probeImage ?? sampleDirectory, new BBox(0, 0, 1, 1), PageKey: "ocr_selection", FieldKey: "selection", Confidence: null)
                    )
                }
            )
        );

        var extracted = new ExtractedInputs(run.RunId, DateTimeOffset.UtcNow, docs);
        return new ExtractedInputsStore().Save(run, extracted);
    }

    private static bool IsSupportedImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp";
    }

    private static bool MatchesOnly(string filePath, string? only)
    {
        if (string.IsNullOrWhiteSpace(only))
        {
            return true;
        }

        var fileName = Path.GetFileName(filePath);
        foreach (var token in only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (fileName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ClassifyByFileNameIfEnabled(string filePath, string? only)
    {
        var enabled = Environment.GetEnvironmentVariable("CHECKMIND_SAMPLE_CLASSIFY_BY_FILENAME");
        if (!string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = Path.GetFileName(filePath);
        if (name.Contains("_72_3", StringComparison.OrdinalIgnoreCase))
        {
            return "0146-06";
        }

        if (name.Contains("_76_3", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("_77_3", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("_78_3", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("_79_3", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("_80_3", StringComparison.OrdinalIgnoreCase))
        {
            return "0146-09";
        }

        return null;
    }

    private static async Task<string?> ClassifyAsync(RunContext run, string storedImagePath, IOcrAdapter ocr, CancellationToken ct)
    {
        if (ocr is MockOcrAdapter)
        {
            return null;
        }

        var bytes = File.ReadAllBytes(storedImagePath);
        var mime = OcrRunner.InferMimeFromPath(storedImagePath);
        var (w, h) = ImageGeometry.GetSize(bytes);

        var headerRoi = ImageGeometry.FromRelative(0.52, 0.00, 0.46, 0.22, w, h);
        var runner = new OcrRunner(ocr, new OcrStore());
        var ocrId = OcrId.Make("classify", Path.GetFileNameWithoutExtension(storedImagePath));
        var (_, ocrResult) = await runner.RunAsync(run, ocrId, bytes, mime, headerRoi, "classify form id", ct);

        var text = ocrResult.Blocks.FirstOrDefault()?.Text ?? "";
        if (text.Contains("0146-06", StringComparison.OrdinalIgnoreCase))
        {
            return "0146-06";
        }
        if (text.Contains("0146-09", StringComparison.OrdinalIgnoreCase))
        {
            return "0146-09";
        }

        return null;
    }
}
