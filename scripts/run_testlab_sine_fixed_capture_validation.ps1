param(
    [switch]$SkipPreflightRecalibration
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName PresentationFramework

$embeddedNoDialogs = (($env:CHECKMIND_EMBEDDED_NO_DIALOGS) + "").Trim()
$embeddedNoDialogs = $embeddedNoDialogs -eq "1" -or $embeddedNoDialogs -eq "true"
$finalRunMarkerPath = Join-Path $env:TEMP "checkmind_testlab_sine_fixed_capture_last_run.txt"

function Decode-Utf8Base64([string]$value)
{
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($value))
}

function Get-LastRunFromMarker
{
    $lastRunPath = Join-Path $env:TEMP "checkmind_testlab_last_run.txt"
    if (!(Test-Path $lastRunPath))
    {
        return ""
    }

    $value = Get-Content -Raw $lastRunPath
    if ($null -eq $value)
    {
        return ""
    }

    return $value.Trim()
}

function Get-LatestProbeRun([string]$runsRoot, [bool]$requireRunResult = $false, [datetime]$notBefore = [datetime]::MinValue, [string]$excludePath = "")
{
    if ([string]::IsNullOrWhiteSpace($runsRoot) -or !(Test-Path $runsRoot))
    {
        return ""
    }

    $dirs = Get-ChildItem -LiteralPath $runsRoot -Directory -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -ne "_config" -and
        ($excludePath -eq "" -or $_.FullName -ne $excludePath) -and
        $_.LastWriteTime -ge $notBefore
    }
    if ($null -eq $dirs -or $dirs.Count -eq 0)
    {
        return ""
    }

    if ($requireRunResult)
    {
        $dirs = $dirs | Where-Object { Test-Path (Join-Path $_.FullName "testlab_run.json") }
        if ($null -eq $dirs -or $dirs.Count -eq 0)
        {
            return ""
        }
    }

    return ($dirs | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

function Resolve-LastRun([string]$runsRoot, [Nullable[datetime]]$businessRunStartTime = $null, [string]$latestRunBeforeStart = "")
{
    if ($null -ne $businessRunStartTime -and $businessRunStartTime.HasValue)
    {
        $notBefore = $businessRunStartTime.Value.AddSeconds(-2)
    }
    else
    {
        $notBefore = [datetime]::MinValue
    }

    for ($i = 0; $i -lt 50; $i++)
    {
        $businessRun = Get-LatestProbeRun $runsRoot $true $notBefore $latestRunBeforeStart
        if (![string]::IsNullOrWhiteSpace($businessRun) -and (Test-Path $businessRun))
        {
            return $businessRun
        }

        Start-Sleep -Milliseconds 100
    }

    for ($i = 0; $i -lt 30; $i++)
    {
        $fromMarker = Get-LastRunFromMarker
        if (![string]::IsNullOrWhiteSpace($fromMarker) -and
            (Test-Path $fromMarker) -and
            (Test-Path (Join-Path $fromMarker "testlab_run.json")))
        {
            return $fromMarker
        }

        Start-Sleep -Milliseconds 100
    }

    $latest = Get-LatestProbeRun $runsRoot $true $notBefore $latestRunBeforeStart
    if (![string]::IsNullOrWhiteSpace($latest) -and (Test-Path $latest))
    {
        return $latest
    }

    return ""
}

function Wait-ForRunCompletion([string]$runDir)
{
    if ([string]::IsNullOrWhiteSpace($runDir) -or !(Test-Path $runDir))
    {
        return $false
    }

    $signals = @(
        (Join-Path $runDir "results.json"),
        (Join-Path $runDir "testlab_phases.log")
    )

    for ($i = 0; $i -lt 100; $i++)
    {
        $ready = $true
        foreach ($signal in $signals)
        {
            if (!(Test-Path $signal))
            {
                $ready = $false
                break
            }
        }

        if ($ready)
        {
            return $true
        }

        Start-Sleep -Milliseconds 100
    }

    return $false
}

$env:CHECKMIND_RUNS_ROOT = Join-Path $repoRoot "artifacts\probe-runs"
$env:CHECKMIND_WORKSTATION_PROFILE_PATH = Join-Path $repoRoot "artifacts\probe-runs\_config\workstation_profile.json"

$messageLines = @(
    (Decode-Utf8Base64 "5Y2z5bCG5byA5aeLIFNpbmUgU2V0dXAg5Zu65a6a5Yy65Z+f6aqM6K+B77ya")
)

if ($SkipPreflightRecalibration)
{
    $messageLines += (Decode-Utf8Base64 "MS4g6Lez6L+H5YmN572u6YeN5qCH77yM55u05o6l5aSN55So5b2T5YmN6aqM6K+B55qE6aG1562+5ZCN")
    $messageLines += (Decode-Utf8Base64 "Mi4g5q2j5byP6L+Q6KGMIENoYW5uZWwgU2V0dXAgKyBTaW5lIFNldHVw")
    $messageLines += (Decode-Utf8Base64 "My4g5Y+q6aqM6K+BIGNvbnRyb2xfcGFuZWzjgIFQcm9maWxlIEVkaXRvcuOAgUFkdmFuY2VkIENvbnRyb2wgU2V0dXAg55qE5oiq5Zu+")
}
else
{
    $messageLines += (Decode-Utf8Base64 "MS4g5aSN55So546w5pyJIENsaWNrUG9pbnQg6YeN5paw5qCH5a6a6aG1562+6aqM55yf562+5ZCN")
    $messageLines += (Decode-Utf8Base64 "Mi4g5q2j5byP6L+Q6KGMIENoYW5uZWwgU2V0dXAgKyBTaW5lIFNldHVw")
    $messageLines += (Decode-Utf8Base64 "My4g5Y+q6aqM6K+BIGNvbnRyb2xfcGFuZWzjgIFQcm9maWxlIEVkaXRvcuOAgUFkdmFuY2VkIENvbnRyb2wgU2V0dXAg55qE5oiq5Zu+")
}

$messageLines += (Decode-Utf8Base64 "NC4g5pys5qyh5LiN6L+b5YWlIE5vdGNoIFByb2ZpbGUg5q2j5byP6ZO+6Lev")
$messageLines += ""
$messageLines += (Decode-Utf8Base64 "5rWB56iL5byA5aeL5ZCO5Lya5o6l566h6byg5qCH77yM6K+35L+d5oyBIFRlc3RsYWIg56qX5Y+j5Y+v6KeB44CC")

$message = $messageLines -join "`r`n"

if (-not $embeddedNoDialogs)
{
    $result = [System.Windows.MessageBox]::Show(
        $message,
        (Decode-Utf8Base64 "Q2hlY2tNaW5kIC0gU2luZSBTZXR1cCDlm7rlrprljLrln5/pqozor4E="),
        [System.Windows.MessageBoxButton]::OKCancel,
        [System.Windows.MessageBoxImage]::Warning
    )

    if ($result -ne [System.Windows.MessageBoxResult]::OK)
    {
        Write-Host (Decode-Utf8Base64 "5bey5Y+W5raI44CC")
        exit 0
    }
}

Remove-Item $finalRunMarkerPath -ErrorAction SilentlyContinue

if (-not $SkipPreflightRecalibration)
{
    Write-Host "Step 1/2: Recalibrate verify signature (reuse ClickPoint)"
    $env:CHECKMIND_CAPTURE_PROMPT = "0"
    $env:CHECKMIND_EMBEDDED_NO_DIALOGS = "1"
    $preflightStartTime = Get-Date
    $latestRunBeforePreflight = Get-LatestProbeRun $env:CHECKMIND_RUNS_ROOT
    powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\calibrate_verify_signature_reuse.ps1")
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
    $preflightRun = Resolve-LastRun $env:CHECKMIND_RUNS_ROOT $preflightStartTime $latestRunBeforePreflight
    if (![string]::IsNullOrWhiteSpace($preflightRun))
    {
        Write-Host ("preflight_run=" + $preflightRun)
    }
}
else
{
    Write-Host "Step 1/1: Run Testlab fixed capture validation (skip preflight recalibration)"
    $preflightRun = ""
}

if (-not $SkipPreflightRecalibration)
{
    Write-Host "Step 2/2: Run Testlab fixed capture validation (no notch profiles)"
}
$env:CHECKMIND_CAPTURE_PROMPT = "0"
$env:CHECKMIND_EMBEDDED_NO_DIALOGS = "1"
$env:CHECKMIND_START_CONFIRM_PROMPT = "0"
$env:CHECKMIND_AUTO_RECALIBRATE_VERIFY_SIGNATURE = "0"
$env:CHECKMIND_RUN_TESTLAB = "1"
$env:CHECKMIND_CAPTURE_MODE = "fixed"
$env:CHECKMIND_FAST_TAB_SWITCH = "1"
$env:CHECKMIND_TESTLAB_TABS = "Channel Setup,Sine Setup"
$env:CHECKMIND_NOTCH_PROFILE_INDEXES = ""
$env:CHECKMIND_TABLE_RESET_TOP_PGUP_COUNT = "10"
$env:CHECKMIND_TABLE_RESET_TOP_PGUP_RETRY_COUNT = "5"
$env:CHECKMIND_TABLE_RESET_TOP_STABLE_CONSECUTIVE = "2"
$env:CHECKMIND_TABLE_KEY_DELAY_MS = "10"
$env:CHECKMIND_TABLE_PAGE_PAUSE_MS = "25"

$project = Join-Path $repoRoot "CheckMind.App\CheckMind.App.csproj"
$appExe = Join-Path $repoRoot "CheckMind.App\bin\Release\net8.0-windows\CheckMind.App.exe"

dotnet build -c Release $project
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

if (!(Test-Path $appExe))
{
    Write-Error ("App exe not found: " + $appExe)
}

$businessRunStartTime = Get-Date
$latestRunBeforeStart = Get-LatestProbeRun $env:CHECKMIND_RUNS_ROOT

$appProcess = Start-Process -FilePath $appExe -PassThru
$appProcess.WaitForExit()
$exitCode = $appProcess.ExitCode

$finalRun = Resolve-LastRun $env:CHECKMIND_RUNS_ROOT $businessRunStartTime $latestRunBeforeStart
if (-not (Wait-ForRunCompletion $finalRun))
{
    Write-Warning "Final run completion evidence not fully ready; fallback to latest run directory."
}

if (![string]::IsNullOrWhiteSpace($finalRun) -and (Test-Path $finalRun))
{
    Set-Content -LiteralPath $finalRunMarkerPath -Value $finalRun -NoNewline
    Write-Host ("final_run=" + $finalRun)
}

if ($exitCode -ne 0)
{
    exit $exitCode
}

if (-not $embeddedNoDialogs)
{
    $finishMessage = if ([string]::IsNullOrWhiteSpace($finalRun))
    {
        (Decode-Utf8Base64 "5Zu65a6a5Yy65Z+f6aqM6K+B5bey5a6M5oiQ44CC")
    }
    elseif ([string]::IsNullOrWhiteSpace($preflightRun))
    {
        @(
            (Decode-Utf8Base64 "5Zu65a6a5Yy65Z+f6aqM6K+B5bey5a6M5oiQ44CC"),
            "preflight_run=skipped",
            "final_run=$finalRun"
        ) -join "`r`n`r`n"
    }
    else
    {
        @(
            (Decode-Utf8Base64 "5Zu65a6a5Yy65Z+f6aqM6K+B5bey5a6M5oiQ44CC"),
            "preflight_run=$preflightRun",
            "final_run=$finalRun"
        ) -join "`r`n`r`n"
    }

    [System.Windows.MessageBox]::Show(
        $finishMessage,
        (Decode-Utf8Base64 "Q2hlY2tNaW5kIC0gU2luZSBTZXR1cCDlm7rlrprljLrln5/pqozor4HlrozmiJA="),
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Information
    ) | Out-Null
}
