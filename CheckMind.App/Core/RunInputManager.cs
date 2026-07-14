using System.IO;

namespace CheckMind.App.Core;

public sealed class RunInputManager
{
    public RunInputRef AddFileToRunInputs(RunContext run, string sourcePath, string kind)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Input file not found.", sourcePath);
        }

        Directory.CreateDirectory(run.InputsDirectory);

        var fileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(run.InputsDirectory, fileName);
        destPath = EnsureUniquePath(destPath);

        File.Copy(sourcePath, destPath);

        return new RunInputRef(
            OriginalPath: sourcePath,
            StoredPath: destPath,
            Kind: kind,
            AddedAtUtc: DateTimeOffset.UtcNow
        );
    }

    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}.{i}{ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to allocate unique destination path.");
    }
}
