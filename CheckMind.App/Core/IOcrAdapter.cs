namespace CheckMind.App.Core;

public interface IOcrAdapter
{
    Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default);
}

