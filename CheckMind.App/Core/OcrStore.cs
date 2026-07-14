using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace CheckMind.App.Core;

public sealed class OcrStore
{
    public string Save(RunContext run, string ocrId, OcrResult result)
    {
        Directory.CreateDirectory(run.OcrDirectory);
        var path = Path.Combine(run.OcrDirectory, $"{ocrId}.json");
        var json = JsonSerializer.Serialize(result, OcrStoreJsonContext.Default.OcrResult);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(OcrResult))]
internal partial class OcrStoreJsonContext : JsonSerializerContext;
