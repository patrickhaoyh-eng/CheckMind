$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

Add-Type -AssemblyName PresentationFramework

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

$env:CHECKMIND_RUNS_ROOT = Join-Path $repoRoot "artifacts\probe-runs"

$message = @(
    (Decode-Utf8Base64 "5Y2z5bCG5byA5aeL5Y+M5q2l6aqk6Ieq5Yqo5rWB56iL77ya"),
    (Decode-Utf8Base64 "MS4g5aSN55So546w5pyJIENsaWNrUG9pbnQg6YeN5qCH5a6a6aqM55yf562+5ZCN"),
    (Decode-Utf8Base64 "Mi4g6L+Q6KGMIENoYW5uZWwgU2V0dXAgKyBTaW5lIFNldHVwIOWPjOmhteWIh+mhteS4jue/u+mhtea1i+ivlQ=="),
    "",
    (Decode-Utf8Base64 "5rWB56iL5byA5aeL5ZCO5Lya5o6l566h6byg5qCHL+mUruebmOOAgg=="),
    (Decode-Utf8Base64 "54K55Ye756Gu5a6a5ZCO56uL5Y2z5byA5aeL77yM5Lit6YCU5LiN5YaN5by55byA5aeL56Gu6K6k5qGG44CC")
) -join "`r`n"

$result = [System.Windows.MessageBox]::Show(
    $message,
    (Decode-Utf8Base64 "Q2hlY2tNaW5kIC0g5LiA6ZSu6L+Q6KGM"),
    [System.Windows.MessageBoxButton]::OKCancel,
    [System.Windows.MessageBoxImage]::Warning
)

if ($result -ne [System.Windows.MessageBoxResult]::OK)
{
    Write-Host (Decode-Utf8Base64 "5bey5Y+W5raI44CC")
    exit 0
}

Write-Host "Running: run_testlab_dual_tabs_pgup_pgdn_autocalib.ps1"
$env:CHECKMIND_CAPTURE_PROMPT = "0"
$env:CHECKMIND_EMBEDDED_NO_DIALOGS = "1"
powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\run_testlab_dual_tabs_pgup_pgdn_autocalib.ps1")
$exitCode = $LASTEXITCODE
Remove-Item Env:\CHECKMIND_EMBEDDED_NO_DIALOGS -ErrorAction SilentlyContinue

$lastRun = Get-LastRunFromMarker
if ([string]::IsNullOrWhiteSpace($lastRun) -or !(Test-Path $lastRun))
{
    for ($i = 0; $i -lt 30; $i++)
    {
        $lastRun = Get-LastRunFromMarker
        if (![string]::IsNullOrWhiteSpace($lastRun) -and (Test-Path $lastRun))
        {
            break
        }

        Start-Sleep -Milliseconds 100
    }

    if ([string]::IsNullOrWhiteSpace($lastRun) -or !(Test-Path $lastRun))
    {
        $lastRun = Get-LatestProbeRun $env:CHECKMIND_RUNS_ROOT
    }
}

if (![string]::IsNullOrWhiteSpace($lastRun))
{
    Write-Host ("last_run=" + $lastRun)
}

if ($exitCode -ne 0)
{
    exit $exitCode
}

$evidenceDir = if (![string]::IsNullOrWhiteSpace($lastRun))
{
    Join-Path $lastRun "screenshots\evidence"
}
else
{
    ""
}

$openTarget = if (![string]::IsNullOrWhiteSpace($evidenceDir) -and (Test-Path $evidenceDir))
{
    $evidenceDir
}
else
{
    $lastRun
}

if (-not [string]::IsNullOrWhiteSpace($openTarget) -and (Test-Path $openTarget))
{
    try
    {
        Invoke-Item -LiteralPath $openTarget
    }
    catch
    {
    }

    try
    {
        Set-Clipboard -Value $lastRun
    }
    catch
    {
    }
}

$finishMessage = if ([string]::IsNullOrWhiteSpace($lastRun))
{
    Decode-Utf8Base64 "6Ieq5Yqo5rWB56iL5bey5a6M5oiQ44CC"
}
else
{
    @(
        Decode-Utf8Base64 "6Ieq5Yqo5rWB56iL5bey5a6M5oiQ44CC"
        "last_run=$lastRun"
        ""
        Decode-Utf8Base64 "54K55Ye75piv56uL5Y2z5omT5byA5L+d5a2Y6Lev5b6E77yf"
    ) -join "`r`n`r`n"
}

[System.Windows.MessageBox]::Show(
    $finishMessage,
    (Decode-Utf8Base64 "Q2hlY2tNaW5kIC0g5LiA6ZSu6L+Q6KGM"),
    [System.Windows.MessageBoxButton]::OK,
    [System.Windows.MessageBoxImage]::Information
) | Out-Null
