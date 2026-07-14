using System.Text;
using System.Text.Json;
using System.IO;

namespace CheckMind.App.Core;

public sealed class ExtractedInputsStore
{
    public string Save(RunContext run, ExtractedInputs extracted)
    {
        var path = Path.Combine(run.RunDirectory, "inputs_extracted.json");
        var json = JsonSerializer.Serialize(extracted, ExtractedInputsJsonContext.Default.ExtractedInputs);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}

