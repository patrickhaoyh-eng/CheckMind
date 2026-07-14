using System.IO;

namespace CheckMind.App.Core;

public sealed class OcrRunner
{
    private readonly IOcrAdapter _ocr;
    private readonly OcrStore _store;

    public OcrRunner(IOcrAdapter ocr, OcrStore store)
    {
        _ocr = ocr;
        _store = store;
    }

    public async Task<(string OcrPath, OcrResult Result)> RunAsync(
        RunContext run,
        string ocrId,
        byte[] imageBytes,
        string imageMime,
        BBox roi,
        string? hint,
        CancellationToken ct = default
    )
    {
        var timeoutSeconds = Environment.GetEnvironmentVariable("CHECKMIND_OCR_TIMEOUT_SECONDS");
        var timeout = int.TryParse(timeoutSeconds, out var v) ? v : 60;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeout, 5, 600)));

        OcrResult result;
        try
        {
            result = await _ocr.RecognizeAsync(new OcrRequest(imageBytes, imageMime, roi, hint), cts.Token);
        }
        catch (Exception ex)
        {
            result = new OcrResult(
                $"error:{ex.GetType().Name}",
                new[]
                {
                    new OcrBlock($"OCR_ERROR:{ex.GetType().Name}", roi, null)
                }
            );
        }
        var ocrPath = _store.Save(run, ocrId, result);
        return (ocrPath, result);
    }

    public static string InferMimeFromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }
}
