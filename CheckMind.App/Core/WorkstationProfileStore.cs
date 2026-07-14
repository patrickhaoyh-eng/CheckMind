using System.Text;
using System.IO;

namespace CheckMind.App.Core;

public sealed class WorkstationProfileStore
{
    public string ProfilePath { get; }

    public WorkstationProfileStore(string profilePath)
    {
        ProfilePath = profilePath;
    }

    public static WorkstationProfileStore CreateDefault()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CheckMind",
            "config"
        );

        var envPath = Environment.GetEnvironmentVariable("CHECKMIND_WORKSTATION_PROFILE_PATH");
        var path = string.IsNullOrWhiteSpace(envPath)
            ? Path.Combine(baseDir, "workstation_profile.json")
            : envPath.Trim();

        return new WorkstationProfileStore(path);
    }

    public WorkstationProfile Load()
    {
        var json = File.ReadAllText(ProfilePath, Encoding.UTF8);
        return WorkstationProfile.FromJson(json);
    }

    public void Save(WorkstationProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(profile, JsonOptions.Default);
        File.WriteAllText(ProfilePath, json, new UTF8Encoding(false));
    }
}
