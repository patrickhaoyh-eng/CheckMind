using System.IO;
using System.Text;

namespace CheckMind.App.Core;

public static class TestlabDebugMarkers
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static string? _currentRunDirectory;

    public static void SetCurrentRunDirectory(string? runDirectory)
    {
        _currentRunDirectory = string.IsNullOrWhiteSpace(runDirectory) ? null : runDirectory;
    }

    public static void WritePhase(string phase, string? runDirectory = null, string? detail = null)
    {
        try
        {
            if (IsDebugPhase(phase) && !IsDebugEnabled())
            {
                return;
            }

            var line = BuildLine(phase, detail);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "checkmind_testlab_phase.txt"), line, Utf8NoBom);

            runDirectory ??= _currentRunDirectory;
            if (string.IsNullOrWhiteSpace(runDirectory))
            {
                return;
            }

            var logPath = Path.Combine(runDirectory, "testlab_phases.log");
            File.AppendAllText(logPath, line + Environment.NewLine, Utf8NoBom);
        }
        catch
        {
        }
    }

    private static bool IsDebugPhase(string phase)
        => phase.StartsWith("overlay.", StringComparison.OrdinalIgnoreCase) ||
           phase.Equals("runner.ocr_bbox_out_of_roi", StringComparison.OrdinalIgnoreCase);

    private static bool IsDebugEnabled()
    {
        var v = Environment.GetEnvironmentVariable("CHECKMIND_DEBUG_ENV");
        return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildLine(string phase, string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return $"{DateTimeOffset.UtcNow:O} | {phase}";
        }

        return $"{DateTimeOffset.UtcNow:O} | {phase} | {detail}";
    }
}
