using System.IO;

namespace CheckMind.App.Core;

public sealed class ScreenshotStore
{
    public string SavePng(RunContext run, string screenshotId, byte[] pngBytes)
    {
        var path = Path.Combine(run.ScreenshotsDirectory, $"{screenshotId}.png");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(path, pngBytes);
        return path;
    }

    public string SaveBytes(RunContext run, string fileName, byte[] bytes)
    {
        var path = Path.Combine(run.ScreenshotsDirectory, fileName);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public string SaveEvidencePng(RunContext run, string screenshotId, byte[] pngBytes)
    {
        return SavePng(run, Path.Combine("evidence", screenshotId), pngBytes);
    }

    public string SaveDebugPng(RunContext run, string screenshotId, byte[] pngBytes)
    {
        return SavePng(run, Path.Combine("debug", screenshotId), pngBytes);
    }
}
