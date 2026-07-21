using System.Text;
using System.Text.Json;
using System.IO;

namespace CheckMind.App.Core;

public sealed class ProfileEditorExtractionStore
{
    public const string DefaultFileName = "profile_editor_extraction.template.json";

    public ProfileEditorExtractionResult Load(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var result = JsonSerializer.Deserialize(json, ProfileEditorExtractionJsonContext.Default.ProfileEditorExtractionResult);
        return result ?? throw new InvalidOperationException("Unable to parse profile editor extraction json");
    }

    public void Save(string path, ProfileEditorExtractionResult result)
    {
        var json = JsonSerializer.Serialize(result, ProfileEditorExtractionJsonContext.Default.ProfileEditorExtractionResult);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public string GetDefaultPath(RunContext run)
        => Path.Combine(run.RunDirectory, DefaultFileName);

    public string SaveToRun(RunContext run, ProfileEditorExtractionResult result)
    {
        var path = GetDefaultPath(run);
        Save(path, result);
        return path;
    }
}
