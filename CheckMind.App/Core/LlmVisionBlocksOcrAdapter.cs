using System.Text.Json;

namespace CheckMind.App.Core;

public sealed class LlmVisionBlocksOcrAdapter : IOcrAdapter
{
    private readonly LlmClient _llm;
    private readonly string _modelId;

    public LlmVisionBlocksOcrAdapter(LlmClient llm, string modelId)
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
        var base64 = Convert.ToBase64String(croppedBytes);
        var imageUrl = $"data:{mime};base64,{base64}";

        var rawHint = (request.Hint ?? "").Trim();
        var hint = string.IsNullOrWhiteSpace(rawHint) ? "" : $"Hint: {rawHint}";
        var isTabsAll = string.Equals(rawHint, "tabs:all", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rawHint, "tabs:list", StringComparison.OrdinalIgnoreCase);

        var instruction = isTabsAll
            ? "You are an OCR engine. Task: read ALL visible bottom worksheet tab labels in the image and return a JSON array of items (0-12). Each item: {\"text\":string,\"x\":int,\"y\":int,\"width\":int,\"height\":int,\"confidence\":number|null}. Coordinates are in pixels within THIS image. Output MUST be ONLY the JSON array and nothing else. The first character must be '[' and the last character must be ']'."
            : "You are an OCR engine. Task: locate the UI element whose text matches the hint (case-insensitive) and return a JSON array (0-3 items). If not found, return []. Each item: {\"text\":string,\"x\":int,\"y\":int,\"width\":int,\"height\":int,\"confidence\":number|null}. Coordinates are in pixels within THIS image. Output MUST be ONLY the JSON array and nothing else. The first character must be '[' and the last character must be ']'.";

        var textPayload = string.IsNullOrWhiteSpace(hint) ? instruction : (instruction + " " + hint);
        var textJson = JsonSerializer.Serialize(textPayload);
        var imageUrlJson = JsonSerializer.Serialize(imageUrl);

        var messagesJson = $$"""
        [
          {
            "role": "user",
            "content": [
              {
                "type": "text",
                "text": {{textJson}}
              },
              {
                "type": "image_url",
                "image_url": { "url": {{imageUrlJson}} }
              }
            ]
          }
        ]
        """;

        var respJson = await _llm.ChatCompletionsAsync(_modelId, messagesJson, cancellationToken);
        var text = ExtractContent(respJson);
        var blocks = ParseBlocks(text);
        return new OcrResult($"llm_vision_blocks:{_modelId}", blocks);
    }

    private static string ExtractContent(string respJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(respJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice0 = choices[0];
                if (choice0.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? "";
                }
            }
        }
        catch
        {
        }

        return respJson;
    }

    private static IReadOnlyList<OcrBlock> ParseBlocks(string json)
    {
        try
        {
            var normalized = NormalizeToJson(json);
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new[] { new OcrBlock(json.Trim(), new BBox(0, 0, 1, 1), null) };
            }

            var list = new List<OcrBlock>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var text = item.TryGetProperty("text", out var t) ? (t.GetString() ?? "") : "";
                var x = item.TryGetProperty("x", out var xx) ? xx.GetInt32() : 0;
                var y = item.TryGetProperty("y", out var yy) ? yy.GetInt32() : 0;
                var w = item.TryGetProperty("width", out var ww) ? ww.GetInt32() : 0;
                var h = item.TryGetProperty("height", out var hh) ? hh.GetInt32() : 0;
                double? c = null;
                if (item.TryGetProperty("confidence", out var cc) && cc.ValueKind is JsonValueKind.Number)
                {
                    c = cc.GetDouble();
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (w <= 0 || h <= 0)
                {
                    continue;
                }

                list.Add(new OcrBlock(text.Trim(), new BBox(x, y, w, h), c));
            }

            return list;
        }
        catch
        {
            return new[] { new OcrBlock(json.Trim(), new BBox(0, 0, 1, 1), null) };
        }
    }

    private static string NormalizeToJson(string s)
    {
        s = (s ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            return "[]";
        }

        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = s.IndexOf('\n');
            if (firstNewline >= 0)
            {
                s = s[(firstNewline + 1)..];
            }

            var lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                s = s[..lastFence];
            }

            s = s.Trim();
        }

        var arrayStart = s.IndexOf('[');
        var arrayEnd = s.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            return s.Substring(arrayStart, arrayEnd - arrayStart + 1);
        }

        var objStart = s.IndexOf('{');
        var objEnd = s.LastIndexOf('}');
        if (objStart >= 0 && objEnd > objStart)
        {
            return "[" + s.Substring(objStart, objEnd - objStart + 1) + "]";
        }

        return "[]";
    }
}
