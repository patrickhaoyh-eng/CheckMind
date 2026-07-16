param(
    [string]$Mode = "foreground",
    [switch]$ResetTop,
    [switch]$AutoClick,
    [string]$ClickMode = "mouse_event",
    [int]$ClickCount = 1
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

$env:CHECKMIND_RUNS_ROOT = Join-Path $repoRoot "artifacts\probe-runs"
$env:CHECKMIND_WORKSTATION_PROFILE_PATH = Join-Path $repoRoot "artifacts\probe-runs\_config\workstation_profile.json"

$env:CHECKMIND_RUN_TESTLAB = "1"
$env:CHECKMIND_RUN_NOTCH_KEY_MODE_PROBE = "1"
$env:CHECKMIND_NOTCH_KEY_PROBE_MODE = $Mode
$env:CHECKMIND_NOTCH_KEY_PROBE_RESET_TOP = $(if ($ResetTop) { "1" } else { "0" })
$env:CHECKMIND_NOTCH_KEY_PROBE_AUTO_CLICK = $(if ($AutoClick) { "1" } else { "0" })
$env:CHECKMIND_NOTCH_KEY_PROBE_CLICK_MODE = $ClickMode
$env:CHECKMIND_NOTCH_KEY_PROBE_CLICK_COUNT = [string]$ClickCount
$env:CHECKMIND_NOTCH_PROFILE_INDEX = "1"
$env:CHECKMIND_NOTCH_KEY_PROBE_WAIT_SECONDS = "8"
$env:CHECKMIND_CAPTURE_PROMPT = "1"
$env:CHECKMIND_OVERLAY = "0"

$project = Join-Path $repoRoot "CheckMind.App\CheckMind.App.csproj"
$appExe = Join-Path $repoRoot "CheckMind.App\bin\Release\net8.0-windows\CheckMind.App.exe"

Write-Host ("CHECKMIND_WORKSTATION_PROFILE_PATH=" + $env:CHECKMIND_WORKSTATION_PROFILE_PATH)
Write-Host ("CHECKMIND_NOTCH_PROFILE_INDEX=" + $env:CHECKMIND_NOTCH_PROFILE_INDEX)
Write-Host ("CHECKMIND_NOTCH_KEY_PROBE_MODE=" + $env:CHECKMIND_NOTCH_KEY_PROBE_MODE)
Write-Host ("CHECKMIND_NOTCH_KEY_PROBE_RESET_TOP=" + $env:CHECKMIND_NOTCH_KEY_PROBE_RESET_TOP)
Write-Host ("CHECKMIND_NOTCH_KEY_PROBE_AUTO_CLICK=" + $env:CHECKMIND_NOTCH_KEY_PROBE_AUTO_CLICK)
Write-Host ("CHECKMIND_NOTCH_KEY_PROBE_CLICK_MODE=" + $env:CHECKMIND_NOTCH_KEY_PROBE_CLICK_MODE)
Write-Host ("CHECKMIND_NOTCH_KEY_PROBE_CLICK_COUNT=" + $env:CHECKMIND_NOTCH_KEY_PROBE_CLICK_COUNT)
Write-Host "Starting notch_profile key mode probe"

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
exit $LASTEXITCODE
