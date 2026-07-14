namespace CheckMind.App.Core;

public sealed class MockOcrAdapter : IOcrAdapter
{
    public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        var blocks = new[]
        {
            new OcrBlock("MOCK_OCR", request.Roi, 1.0)
        };

        return Task.FromResult(new OcrResult("mock", blocks));
    }
}

