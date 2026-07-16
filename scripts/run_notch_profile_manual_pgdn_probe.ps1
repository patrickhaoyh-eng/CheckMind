$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

$env:CHECKMIND_RUNS_ROOT = Join-Path $repoRoot "artifacts\probe-runs"
$env:CHECKMIND_WORKSTATION_PROFILE_PATH = Join-Path $repoRoot "artifacts\probe-runs\_config\workstation_profile.json"

$env:CHECKMIND_RUN_TESTLAB = "1"
$env:CHECKMIND_RUN_NOTCH_MANUAL_PGDN_PROBE = "1"
$env:CHECKMIND_NOTCH_PROFILE_INDEX = "1"
$env:CHECKMIND_NOTCH_MANUAL_PGDN_WAIT_SECONDS = "8"
$env:CHECKMIND_NOTCH_MANUAL_AUTO_FOCUS = "0"
$env:CHECKMIND_CAPTURE_PROMPT = "1"
$env:CHECKMIND_OVERLAY = "0"

$project = Join-Path $repoRoot "CheckMind.App\CheckMind.App.csproj"
$appExe = Join-Path $repoRoot "CheckMind.App\bin\Release\net8.0-windows\CheckMind.App.exe"

Write-Host ("CHECKMIND_WORKSTATION_PROFILE_PATH=" + $env:CHECKMIND_WORKSTATION_PROFILE_PATH)
Write-Host ("CHECKMIND_NOTCH_PROFILE_INDEX=" + $env:CHECKMIND_NOTCH_PROFILE_INDEX)
Write-Host "Starting notch_profile manual PgDn probe"

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
