using System.Text;
using System.Text.Json;
using System.IO;

namespace CheckMind.App.Core;

public sealed class RunStorage
{
    private readonly string _runsRootDirectory;

    public RunStorage(string? runsRootDirectory = null)
    {
        _runsRootDirectory = runsRootDirectory ?? GetDefaultRunsRootDirectory();
    }

    public RunContext CreateRun(RunCreateOptions? options = null)
    {
        options ??= new RunCreateOptions();

        var runId = string.IsNullOrWhiteSpace(options.RunId)
            ? Guid.NewGuid().ToString("N")
            : options.RunId.Trim();

        var runDirectory = Path.Combine(_runsRootDirectory, runId);

        Directory.CreateDirectory(runDirectory);

        var inputsDirectory = Path.Combine(runDirectory, "inputs");
        var screenshotsDirectory = Path.Combine(runDirectory, "screenshots");
        var cropsDirectory = Path.Combine(runDirectory, "crops");
        var ocrDirectory = Path.Combine(runDirectory, "ocr");

        Directory.CreateDirectory(inputsDirectory);
        Directory.CreateDirectory(screenshotsDirectory);
        Directory.CreateDirectory(cropsDirectory);
        Directory.CreateDirectory(ocrDirectory);

        var metaPath = Path.Combine(runDirectory, "meta.json");
        if (!File.Exists(metaPath))
        {
            var meta = new RunMeta(
                RunId: runId,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                TrialId: options.TrialId,
                OperatorName: options.OperatorName,
                Inputs: Array.Empty<RunInputRef>()
            );

            var metaJson = JsonSerializer.Serialize(meta, RunMetaJsonContext.Default.RunMeta);
            File.WriteAllText(metaPath, metaJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        var resultsPath = Path.Combine(runDirectory, "results.json");
        if (!File.Exists(resultsPath))
        {
            var resultsSkeleton = $$"""
            {
              "runId": "{{runId}}",
              "generatedAtUtc": "{{DateTimeOffset.UtcNow:O}}",
              "alerts": []
            }
            """;
            File.WriteAllText(resultsPath, resultsSkeleton, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return new RunContext(
            runId,
            runDirectory,
            inputsDirectory,
            screenshotsDirectory,
            cropsDirectory,
            ocrDirectory,
            metaPath,
            resultsPath
        );
    }

    private static string GetDefaultRunsRootDirectory()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("CHECKMIND_RUNS_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return overrideRoot.Trim();
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CheckMind", "data", "runs");
    }
}

public sealed record RunCreateOptions(
    string? TrialId = null,
    string? OperatorName = null,
    string? RunId = null
);

public sealed record RunContext(
    string RunId,
    string RunDirectory,
    string InputsDirectory,
    string ScreenshotsDirectory,
    string CropsDirectory,
    string OcrDirectory,
    string MetaPath,
    string ResultsPath
);
