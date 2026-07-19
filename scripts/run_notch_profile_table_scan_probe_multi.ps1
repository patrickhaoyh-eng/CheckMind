param(
    [string]$NotchProfileIndexes = "1,2"
)

$ErrorActionPreference = "Stop"

function Parse-IndexList {
    param(
        [string]$Raw
    )

    $values = @()
    foreach ($match in [regex]::Matches(($Raw + ""), '\d+')) {
        $token = $match.Value
        $parsed = 0
        if (-not [int]::TryParse($token, [ref]$parsed)) {
            throw "Invalid Notch Profile index: $token"
        }
        if ($parsed -lt 1) {
            throw "Notch Profile index must be >= 1: $token"
        }
        $values += $parsed
    }

    if ($values.Count -eq 0) {
        throw "No valid Notch Profile indexes were provided."
    }

    return @($values | Select-Object -Unique)
}

function Resolve-LatestRun {
    param(
        [string]$RunsRoot
    )

    $latest = Get-ChildItem -Path $RunsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        return $null
    }

    return $latest.FullName
}

function Wait-ForNewRun {
    param(
        [string]$RunsRoot,
        [string]$BeforeRun,
        [datetime]$StartedAtUtc,
        [int]$TimeoutSeconds = 10
    )

    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([datetime]::UtcNow -lt $deadline) {
        $latest = Get-ChildItem -Path $RunsRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTimeUtc -ge $StartedAtUtc.AddSeconds(-1) } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1

        if ($null -ne $latest -and $latest.FullName -ne $BeforeRun) {
            return $latest.FullName
        }

        Start-Sleep -Milliseconds 300
    }

    return $null
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "CheckMind.App\CheckMind.App.csproj"
$appExe = Join-Path $repoRoot "CheckMind.App\bin\Release\net8.0-windows\CheckMind.App.exe"
$runsRoot = Join-Path $repoRoot "artifacts\probe-runs"
$profilePath = Join-Path $repoRoot "artifacts\probe-runs\_config\workstation_profile.json"

$indexes = Parse-IndexList $NotchProfileIndexes

$env:CHECKMIND_RUNS_ROOT = $runsRoot
$env:CHECKMIND_WORKSTATION_PROFILE_PATH = $profilePath
$env:CHECKMIND_RUN_TESTLAB = "1"
$env:CHECKMIND_RUN_NOTCH_TABLE_SCAN_PROBE = "1"
$env:CHECKMIND_CAPTURE_PROMPT = "1"
$env:CHECKMIND_OVERLAY = "0"

Write-Host ("CHECKMIND_WORKSTATION_PROFILE_PATH=" + $env:CHECKMIND_WORKSTATION_PROFILE_PATH)
Write-Host ("CHECKMIND_NOTCH_PROFILE_INDEXES=" + (($indexes | ForEach-Object { $_.ToString() }) -join ","))
Write-Host "Starting notch_profile table scan probe (multi)"

dotnet build -c Release $project
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (!(Test-Path $appExe)) {
    Write-Error ("App exe not found: " + $appExe)
}

$runSummaries = @()

foreach ($index in $indexes) {
    $env:CHECKMIND_NOTCH_PROFILE_INDEX = $index.ToString()
    Write-Host ("")
    Write-Host ("=== notch_profile_table_scan_probe row=" + $index + " ===")

    $beforeRun = Resolve-LatestRun $runsRoot
    $startedAtUtc = [datetime]::UtcNow

    $process = Start-Process -FilePath $appExe -PassThru
    $process.WaitForExit()

    if ($process.ExitCode -ne 0) {
        exit $process.ExitCode
    }

    $afterRun = Wait-ForNewRun -RunsRoot $runsRoot -BeforeRun $beforeRun -StartedAtUtc $startedAtUtc
    if ($null -eq $afterRun) {
        throw "Failed to resolve new run directory for row=$index"
    }

    Write-Host ("last_run=" + $afterRun)
    $runSummaries += [pscustomobject]@{
        RowIndex = $index
        RunPath = $afterRun
    }
}

Write-Host ""
Write-Host "Probe runs completed:"
foreach ($summary in $runSummaries) {
    Write-Host ("row=" + $summary.RowIndex + ";run=" + $summary.RunPath)
}
