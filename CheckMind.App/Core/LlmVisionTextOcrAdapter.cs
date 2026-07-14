using System.IO;
using System.Text.Json;

namespace CheckMind.App.Core;

public sealed class LlmVisionTextOcrAdapter : IOcrAdapter
{
    private readonly LlmClient _llm;
    private readonly string _modelId;

    public LlmVisionTextOcrAdapter(LlmClient llm, string modelId)
    {
        _llm = llm;
        _modelId = modelId;
    }

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        var cropEnabled = Environment.GetEnvironmentVariable("CHECKMIND_OCR_CROP");
        var shouldCrop = string.Equals(cropEnabled, "1", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(cropEnabled, "true", StringComparison.OrdinalIgnoreCase);

        var croppedBytes = shouldCrop
            ? (ImageCropper.TryCropToPngBytes(request.ImageBytes, request.Roi) ?? request.ImageBytes)
            : request.ImageBytes;
        var mime = croppedBytes == request.ImageBytes ? request.ImageMime : "image/png";

        var imageBase64 = Convert.ToBase64String(croppedBytes);
        var imageUrl = $"data:{mime};base64,{imageBase64}";

        var hint = string.IsNullOrWhiteSpace(request.Hint) ? "" : $"Hint: {request.Hint}";

        var messagesJson = $$"""
        [
          {
            "role": "user",
            "content": [
              {
                "type": "text",
                "text": "Extract all visible text from the image region. Output ONLY the extracted text. No analysis, no reasoning, no markdown. {{hint}}"
              },
              {
                "type": "image_url",
                "image_url": { "url": "{{imageUrl}}" }
              }
            ]
          }
        ]
        """;

        var respJson = await _llm.ChatCompletionsAsync(_modelId, messagesJson, cancellationToken);

        var text = respJson;
        try
        {
            using var doc = JsonDocument.Parse(respJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice0 = choices[0];
                if (choice0.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                {
                    text = content.GetString() ?? "";
                }
            }
        }
        catch
        {
        }

        var block = new OcrBlock(text.Trim(), request.Roi, null);
        return new OcrResult($"llm_vision_text:{_modelId}", new[] { block });
    }
}
