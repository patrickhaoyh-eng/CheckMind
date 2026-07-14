using System.Text.Json;

namespace CheckMind.App.Core;

public sealed record WorkstationProfile(
    WorkstationProfileEnvironment Environment,
    WorkstationProfileWindow Window,
    WorkstationProfileTolerances Tolerances,
    WorkstationProfileNavigation? Navigation = null,
    WorkstationDialogActionProfile[]? DialogActions = null,
    WorkstationPageProfile[]? Pages = null
)
{
    public static WorkstationProfile FromJson(string json)
        => JsonSerializer.Deserialize<WorkstationProfile>(json, JsonOptions.Default)
           ?? throw new InvalidOperationException("Invalid WorkstationProfile json.");

    public WorkstationProfile NormalizeForStorage()
    {
        var normalizedPages = (Pages ?? [])
            .Select(static page => page.NormalizeForStorage())
            .ToArray();

        return this with { Pages = normalizedPages };
    }

    public WorkstationTabClickTarget? FindTabClickTarget(string tabName)
    {
        var targetNorm = WorkstationProfileKeys.Normalize(tabName);
        foreach (var item in Navigation?.TabClickPoints ?? [])
        {
            if (WorkstationProfileKeys.Normalize(item.TabName) == targetNorm)
            {
                return item;
            }
        }

        return null;
    }

    public WorkstationDialogActionProfile? FindDialogAction(string dialogKey)
    {
        var targetNorm = WorkstationProfileKeys.Normalize(dialogKey);
        foreach (var item in DialogActions ?? [])
        {
            if (WorkstationProfileKeys.Normalize(item.DialogKey) == targetNorm)
            {
                return item;
            }
        }

        return null;
    }

    public WorkstationPageProfile? FindPageProfile(string tabName)
    {
        var targetNorm = WorkstationProfileKeys.Normalize(tabName);
        foreach (var item in Pages ?? [])
        {
            if (WorkstationProfileKeys.Normalize(item.TabName) == targetNorm)
            {
                return item;
            }
        }

        return null;
    }
}

public sealed record WorkstationProfileEnvironment(
    int TargetMonitorIndex,
    int TargetWidth,
    int TargetHeight,
    int DpiScalePercent
);

public sealed record WorkstationProfileWindow(
    bool MustBeMaximized,
    BBox WindowRectScreen
);

public sealed record WorkstationProfileTolerances(
    int PixelTolerance
);

public sealed record WorkstationProfileNavigation(
    WorkstationTabClickTarget[]? TabClickPoints = null
);

public sealed record WorkstationTabClickTarget(
    string TabName,
    WindowPoint? ClickPoint = null
);

public sealed record WorkstationDialogActionProfile(
    string DialogKey,
    WindowPoint? ClickPoint = null,
    BBox? VerifyRoiWindow = null,
    string? VerifySha256 = null
);

public sealed record WorkstationPageProfile(
    string TabName,
    BBox? CaptureRoiWindow = null,
    WindowPoint? ScrollAnchor = null,
    BBox? VerifyRoiWindow = null,
    string? VerifySha256 = null,
    string? TopSerialVerifySha256 = null,
    WorkstationCaptureTarget[]? CaptureTargets = null,
    WorkstationVerifyTarget[]? VerifyTargets = null
)
{
    public WorkstationCaptureTarget? FindCaptureTarget(string key)
    {
        var targetNorm = WorkstationProfileKeys.Normalize(key);
        foreach (var item in CaptureTargets ?? [])
        {
            if (WorkstationProfileKeys.Normalize(item.Key) == targetNorm)
            {
                return item;
            }
        }

        if (CaptureRoiWindow is not null && WorkstationProfileKeys.IsDefaultCaptureKey(targetNorm))
        {
            return new WorkstationCaptureTarget("default", CaptureRoiWindow);
        }

        return null;
    }

    public WorkstationVerifyTarget? FindVerifyTarget(string key)
    {
        var targetNorm = WorkstationProfileKeys.Normalize(key);
        foreach (var item in VerifyTargets ?? [])
        {
            if (WorkstationProfileKeys.Normalize(item.Key) == targetNorm)
            {
                return item;
            }
        }

        if (VerifyRoiWindow is not null && !string.IsNullOrWhiteSpace(VerifySha256) && WorkstationProfileKeys.IsDefaultVerifyKey(targetNorm))
        {
            return new WorkstationVerifyTarget("default", VerifyRoiWindow, VerifySha256);
        }

        if (!string.IsNullOrWhiteSpace(TopSerialVerifySha256) && WorkstationProfileKeys.IsTopSerialVerifyKey(targetNorm))
        {
            return new WorkstationVerifyTarget("top_serial", null, TopSerialVerifySha256);
        }

        return null;
    }

    public WorkstationPageProfile NormalizeForStorage()
    {
        var captureTargets = NormalizeCaptureTargets();
        var verifyTargets = NormalizeVerifyTargets();

        var hasStructuredScanCapture = captureTargets.Any(static target =>
            WorkstationProfileKeys.IsTableScanCaptureKey(WorkstationProfileKeys.Normalize(target.Key)) &&
            target.RoiWindow is not null);
        var hasStructuredTabVerify = verifyTargets.Any(static target =>
            WorkstationProfileKeys.IsDefaultVerifyKey(WorkstationProfileKeys.Normalize(target.Key)) &&
            target.RoiWindow is not null &&
            !string.IsNullOrWhiteSpace(target.Sha256));
        var hasStructuredTopSerial = verifyTargets.Any(static target =>
            WorkstationProfileKeys.IsTopSerialVerifyKey(WorkstationProfileKeys.Normalize(target.Key)) &&
            target.RoiWindow is not null &&
            !string.IsNullOrWhiteSpace(target.Sha256));

        return this with
        {
            CaptureRoiWindow = hasStructuredScanCapture ? null : CaptureRoiWindow,
            ScrollAnchor = ScrollAnchor,
            VerifyRoiWindow = hasStructuredTabVerify ? null : VerifyRoiWindow,
            VerifySha256 = hasStructuredTabVerify ? null : VerifySha256,
            TopSerialVerifySha256 = hasStructuredTopSerial ? null : TopSerialVerifySha256,
            CaptureTargets = captureTargets.Length == 0 ? null : captureTargets,
            VerifyTargets = verifyTargets.Length == 0 ? null : verifyTargets
        };
    }

    private WorkstationCaptureTarget[] NormalizeCaptureTargets()
    {
        var targets = (CaptureTargets ?? [])
            .ToList();

        if (CaptureRoiWindow is not null)
        {
            UpsertCaptureTarget(targets, "table_scan", CaptureRoiWindow);
        }

        return targets
            .OrderBy(static item => WorkstationProfileKeys.Normalize(item.Key))
            .ToArray();
    }

    private WorkstationVerifyTarget[] NormalizeVerifyTargets()
    {
        var targets = (VerifyTargets ?? [])
            .ToList();

        if (VerifyRoiWindow is not null && !string.IsNullOrWhiteSpace(VerifySha256))
        {
            UpsertVerifyTarget(targets, "tab_verify", VerifyRoiWindow, VerifySha256);
        }

        if (!string.IsNullOrWhiteSpace(TopSerialVerifySha256))
        {
            UpsertVerifyTarget(targets, "top_serial", null, TopSerialVerifySha256);
        }

        return targets
            .OrderBy(static item => WorkstationProfileKeys.Normalize(item.Key))
            .ToArray();
    }

    private static void UpsertCaptureTarget(List<WorkstationCaptureTarget> targets, string key, BBox? roiWindow)
    {
        var keyNorm = WorkstationProfileKeys.Normalize(key);
        for (var i = 0; i < targets.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(targets[i].Key) != keyNorm)
            {
                continue;
            }

            targets[i] = targets[i] with { RoiWindow = targets[i].RoiWindow ?? roiWindow };
            return;
        }

        targets.Add(new WorkstationCaptureTarget(key, roiWindow));
    }

    private static void UpsertVerifyTarget(List<WorkstationVerifyTarget> targets, string key, BBox? roiWindow, string? sha256)
    {
        var keyNorm = WorkstationProfileKeys.Normalize(key);
        for (var i = 0; i < targets.Count; i++)
        {
            if (WorkstationProfileKeys.Normalize(targets[i].Key) != keyNorm)
            {
                continue;
            }

            targets[i] = targets[i] with
            {
                RoiWindow = targets[i].RoiWindow ?? roiWindow,
                Sha256 = string.IsNullOrWhiteSpace(targets[i].Sha256) ? sha256 : targets[i].Sha256
            };
            return;
        }

        targets.Add(new WorkstationVerifyTarget(key, roiWindow, sha256));
    }
}

public sealed record WorkstationCaptureTarget(
    string Key,
    BBox? RoiWindow = null
);

public sealed record WorkstationVerifyTarget(
    string Key,
    BBox? RoiWindow = null,
    string? Sha256 = null
);

public readonly record struct WindowPoint(int X, int Y);

internal static class WorkstationProfileKeys
{
    public static string Normalize(string value)
    {
        var chars = value.Where(ch => !char.IsWhiteSpace(ch) && ch is not ('-' or '_' or ':' or '：'))
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    public static bool IsDefaultCaptureKey(string normalizedKey)
        => normalizedKey is "default" or "main" or "maintable" or "table" or "capture";

    public static bool IsTableScanCaptureKey(string normalizedKey)
        => normalizedKey is "tablescan" or "scan" or "tablecapture";

    public static bool IsDefaultVerifyKey(string normalizedKey)
        => normalizedKey is "default" or "main" or "title" or "tabverify" or "verify";

    public static bool IsTopSerialVerifyKey(string normalizedKey)
        => normalizedKey is "topserial" or "topserialverify" or "topverify";
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
