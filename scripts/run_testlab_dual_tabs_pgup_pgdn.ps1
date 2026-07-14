$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

$env:CHECKMIND_RUNS_ROOT = Join-Path $repoRoot "artifacts\probe-runs"
$env:CHECKMIND_WORKSTATION_PROFILE_PATH = Join-Path $repoRoot "artifacts\probe-runs\_config\workstation_profile.json"

$env:CHECKMIND_RUN_TESTLAB = "1"
$env:CHECKMIND_CAPTURE_MODE = "fixed"
$env:CHECKMIND_FAST_TAB_SWITCH = "1"
$env:CHECKMIND_TESTLAB_TABS = "Channel Setup,Sine Setup"

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

function Open-RunTarget([string]$runDir)
{
    if ([string]::IsNullOrWhiteSpace($runDir) -or !(Test-Path $runDir))
    {
        return
    }

    $evidenceDir = Join-Path $runDir "screenshots\evidence"
    $openTarget = if (Test-Path $evidenceDir) { $evidenceDir } else { $runDir }

    try
    {
        Invoke-Item -LiteralPath $openTarget
    }
    catch
    {
    }

    try
    {
        Set-Clipboard -Value $runDir
    }
    catch
    {
    }
}

$lastRunMarker = Join-Path $env:TEMP "checkmind_testlab_last_run.txt"
$errorMarker = Join-Path $env:TEMP "checkmind_testlab_error.txt"
Remove-Item $lastRunMarker -ErrorAction SilentlyContinue
Remove-Item $errorMarker -ErrorAction SilentlyContinue

Write-Host ("CHECKMIND_RUNS_ROOT=" + $env:CHECKMIND_RUNS_ROOT)
Write-Host ("CHECKMIND_WORKSTATION_PROFILE_PATH=" + $env:CHECKMIND_WORKSTATION_PROFILE_PATH)
Write-Host ("CHECKMIND_RUN_TESTLAB=" + $env:CHECKMIND_RUN_TESTLAB)
Write-Host ("CHECKMIND_TESTLAB_TABS=" + $env:CHECKMIND_TESTLAB_TABS)
Write-Host ("CHECKMIND_CAPTURE_PROMPT=" + $env:CHECKMIND_CAPTURE_PROMPT)

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
    $embeddedNoDialogs = (($env:CHECKMIND_EMBEDDED_NO_DIALOGS) + "").Trim()
    $embeddedNoDialogs = $embeddedNoDialogs -eq "1" -or $embeddedNoDialogs -eq "true"
    if (-not $embeddedNoDialogs)
    {
        Open-RunTarget $lastRun
    }
}

if ($exitCode -ne 0)
{
    exit $exitCode
}
