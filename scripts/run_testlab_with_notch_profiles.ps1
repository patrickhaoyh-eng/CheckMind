param(
    [string]$NotchProfileIndexes = "1"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName PresentationFramework

$embeddedNoDialogs = (($env:CHECKMIND_EMBEDDED_NO_DIALOGS) + "").Trim()
$embeddedNoDialogs = $embeddedNoDialogs -eq "1" -or $embeddedNoDialogs -eq "true"

$startConfirmPrompt = (($env:CHECKMIND_START_CONFIRM_PROMPT) + "").Trim()
if ([string]::IsNullOrWhiteSpace($startConfirmPrompt))
{
    $startConfirmPrompt = "1"
}
$startConfirmPrompt = $startConfirmPrompt -eq "1" -or $startConfirmPrompt -eq "true"

$autoRecalibrateVerify = (($env:CHECKMIND_AUTO_RECALIBRATE_VERIFY_SIGNATURE) + "").Trim()
if ([string]::IsNullOrWhiteSpace($autoRecalibrateVerify))
{
    $autoRecalibrateVerify = "1"
}
$autoRecalibrateVerify = $autoRecalibrateVerify -eq "1" -or $autoRecalibrateVerify -eq "true"

$env:CHECKMIND_RUNS_ROOT = Join-Path $repoRoot "artifacts\probe-runs"
$env:CHECKMIND_WORKSTATION_PROFILE_PATH = Join-Path $repoRoot "artifacts\probe-runs\_config\workstation_profile.json"

$env:CHECKMIND_RUN_TESTLAB = "1"
$env:CHECKMIND_CAPTURE_MODE = "fixed"
$env:CHECKMIND_FAST_TAB_SWITCH = "1"
$env:CHECKMIND_TESTLAB_TABS = "Channel Setup,Sine Setup"
$env:CHECKMIND_NOTCH_PROFILE_INDEXES = $NotchProfileIndexes

if ([string]::IsNullOrWhiteSpace($env:CHECKMIND_CAPTURE_PROMPT))
{
    $env:CHECKMIND_CAPTURE_PROMPT = "1"
}
$env:CHECKMIND_TABLE_RESET_TOP_PGUP_COUNT = "10"
$env:CHECKMIND_TABLE_RESET_TOP_PGUP_RETRY_COUNT = "5"
$env:CHECKMIND_TABLE_RESET_TOP_STABLE_CONSECUTIVE = "2"
$env:CHECKMIND_TABLE_KEY_DELAY_MS = "10"
$env:CHECKMIND_TABLE_PAGE_PAUSE_MS = "25"

$project = Join-Path $repoRoot "CheckMind.App\CheckMind.App.csproj"
$appExe = Join-Path $repoRoot "CheckMind.App\bin\Release\net8.0-windows\CheckMind.App.exe"

function Get-LastRunFromMarker
{
    $lastRunPath = Join-Path $env:TEMP "checkmind_testlab_last_run.txt"
    if (!(Test-Path $lastRunPath))
    {
        return ""
    }

    $value = (Get-Content -Raw $lastRunPath)
    if ($null -eq $value)
    {
        return ""
    }

    return $value.Trim()
}

function Get-LatestProbeRun([string]$runsRoot)
{
    if ([string]::IsNullOrWhiteSpace($runsRoot) -or !(Test-Path $runsRoot))
    {
        return ""
    }

    $dirs = Get-ChildItem -LiteralPath $runsRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "_config" }
    if ($null -eq $dirs -or $dirs.Count -eq 0)
    {
        return ""
    }

    return ($dirs | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

function Resolve-LastRun([string]$runsRoot)
{
    $markerPath = Join-Path $env:TEMP "checkmind_testlab_last_run.txt"
    for ($i = 0; $i -lt 30; $i++)
    {
        $fromMarker = Get-LastRunFromMarker
        if (![string]::IsNullOrWhiteSpace($fromMarker) -and (Test-Path $fromMarker))
        {
            return $fromMarker
        }

        Start-Sleep -Milliseconds 100
    }

    $latest = Get-LatestProbeRun $runsRoot
    if (![string]::IsNullOrWhiteSpace($latest) -and (Test-Path $latest))
    {
        return $latest
    }

    return ""
}

function Decode-Utf8Base64([string]$value)
{
    return [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($value))
}

if (-not $embeddedNoDialogs -and $startConfirmPrompt)
{
    $message = @(
        (Decode-Utf8Base64 "5Y2z5bCG5byA5aeLIENoYW5uZWwgU2V0dXDjgIFTaW5lIFNldHVwIOS4jiBOb3RjaCBQcm9maWxlcyDnmoToh6rliqjmipPlj5bjgII="),
        (Decode-Utf8Base64 "54K55Ye74oCc56Gu5a6a4oCd5ZCO77yM6ISa5pys5Lya5o6l566h6byg5qCH5bm25bCGIFRlc3RsYWIg5YiH5Yiw5YmN5Y+w44CC"),
        "",
        ((Decode-Utf8Base64 "Tm90Y2ggUHJvZmlsZSDluo/lj7c6IA==") + $NotchProfileIndexes),
        "",
        (Decode-Utf8Base64 "6K+35YGc5q2i56e75Yqo6byg5qCH77yM5bm25L+d5oyBIFRlc3RsYWIg56qX5Y+j5Y+v6KeB44CC")
    ) -join "`r`n"

    $result = [System.Windows.MessageBox]::Show(
        $message,
        (Decode-Utf8Base64 "Q2hlY2tNaW5kIC0g5byA5aeL6Ieq5Yqo5oqT5Y+W"),
        [System.Windows.MessageBoxButton]::OKCancel,
        [System.Windows.MessageBoxImage]::Warning
    )

    if ($result -ne [System.Windows.MessageBoxResult]::OK)
    {
        Write-Host (Decode-Utf8Base64 "5bey5Y+W5raI44CC")
        exit 0
    }
}

$env:CHECKMIND_CAPTURE_PROMPT = "0"
$env:CHECKMIND_EMBEDDED_NO_DIALOGS = "1"

if ($autoRecalibrateVerify)
{
    Write-Host "Preflight: recalibrate verify signature (reuse ClickPoint)"
    powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\calibrate_verify_signature_reuse.ps1")
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

Write-Host ("CHECKMIND_RUNS_ROOT=" + $env:CHECKMIND_RUNS_ROOT)
Write-Host ("CHECKMIND_WORKSTATION_PROFILE_PATH=" + $env:CHECKMIND_WORKSTATION_PROFILE_PATH)
Write-Host ("CHECKMIND_RUN_TESTLAB=" + $env:CHECKMIND_RUN_TESTLAB)
Write-Host ("CHECKMIND_TESTLAB_TABS=" + $env:CHECKMIND_TESTLAB_TABS)
Write-Host ("CHECKMIND_NOTCH_PROFILE_INDEXES=" + $env:CHECKMIND_NOTCH_PROFILE_INDEXES)
Write-Host ("CHECKMIND_CAPTURE_PROMPT=" + $env:CHECKMIND_CAPTURE_PROMPT)
Write-Host ("CHECKMIND_AUTO_RECALIBRATE_VERIFY_SIGNATURE=" + $autoRecalibrateVerify)

dotnet build -c Release $project
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

if (!(Test-Path $appExe))
{
    Write-Error ("App exe not found: " + $appExe)
}

& $appExe
$exitCode = $LASTEXITCODE

$lastRun = Resolve-LastRun $env:CHECKMIND_RUNS_ROOT
if (![string]::IsNullOrWhiteSpace($lastRun))
{
    Write-Host ("last_run=" + $lastRun)
    if (-not $embeddedNoDialogs)
    {
        try
        {
            Invoke-Item -LiteralPath $lastRun
        }
        catch
        {
        }
    }
}

if ($exitCode -ne 0)
{
    exit $exitCode
}
