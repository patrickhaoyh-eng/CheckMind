using System.Text;
using System.Text.Json;
using System.IO;

namespace CheckMind.App.Core;

public sealed class RunMetaStore
{
    public RunMeta Load(string metaPath)
    {
        var json = File.ReadAllText(metaPath, Encoding.UTF8);
        var meta = JsonSerializer.Deserialize(json, RunMetaJsonContext.Default.RunMeta);
        return meta ?? throw new InvalidOperationException("Unable to parse meta.json");
    }

    public void Save(string metaPath, RunMeta meta)
    {
        var json = JsonSerializer.Serialize(meta, RunMetaJsonContext.Default.RunMeta);
        File.WriteAllText(metaPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public RunMeta AppendInput(RunContext run, RunInputRef input)
    {
        var meta = Load(run.MetaPath);
        var inputs = meta.Inputs?.ToList() ?? new List<RunInputRef>();
        inputs.Add(input);
        var updated = meta with { Inputs = inputs };
        Save(run.MetaPath, updated);
        return updated;
    }
}
