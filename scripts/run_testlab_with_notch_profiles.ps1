param(
    [string]$NotchProfileIndexes = "",
    [int]$NotchProfileCount = 0
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Windows.Forms

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

$notchProfileDisplay = ""
if ($NotchProfileCount -gt 0)
{
    $env:CHECKMIND_NOTCH_PROFILE_COUNT = $NotchProfileCount.ToString()
    Remove-Item Env:CHECKMIND_NOTCH_PROFILE_INDEXES -ErrorAction SilentlyContinue
    Remove-Item Env:CHECKMIND_NOTCH_PROFILE_INDEX -ErrorAction SilentlyContinue
    $notchProfileDisplay = ("1.." + $NotchProfileCount + " (notchProfileCount=" + $NotchProfileCount + ")")
}
else
{
    if ([string]::IsNullOrWhiteSpace($NotchProfileIndexes))
    {
        $NotchProfileIndexes = "1"
    }

    $env:CHECKMIND_NOTCH_PROFILE_INDEXES = $NotchProfileIndexes
    Remove-Item Env:CHECKMIND_NOTCH_PROFILE_COUNT -ErrorAction SilentlyContinue
    $notchProfileDisplay = $NotchProfileIndexes
}

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

function Resolve-LastRun([string]$runsRoot, [datetime]$businessRunStartTime, [string]$latestRunBeforeStart)
{
    $markerPath = Join-Path $env:TEMP "checkmind_testlab_last_run.txt"
    $notBefore = $businessRunStartTime.AddSeconds(-2)

    for ($i = 0; $i -lt 30; $i++)
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

function Get-RunResult([string]$runDirectory)
{
    if ([string]::IsNullOrWhiteSpace($runDirectory))
    {
        return $null
    }

    $resultPath = Join-Path $runDirectory "testlab_run.json"
    if (!(Test-Path $resultPath))
    {
        return $null
    }

    try
    {
        return (Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json)
    }
    catch
    {
        return $null
    }
}

function Decode-Utf8Base64([string]$value)
{
    return [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($value))
}

function Show-TopMostAlert([string]$message, [string]$caption)
{
    $form = New-Object System.Windows.Forms.Form
    try
    {
        $form.TopMost = $true
        $form.ShowInTaskbar = $false
        $form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
        $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal
        $form.Size = New-Object System.Drawing.Size(1, 1)
        $form.Opacity = 0
        $form.Show()
        $form.Activate()
        [System.Windows.Forms.MessageBox]::Show(
            $form,
            $message,
            $caption,
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning,
            [System.Windows.Forms.MessageBoxDefaultButton]::Button1
        ) | Out-Null
    }
    finally
    {
        if ($null -ne $form)
        {
            $form.Close()
            $form.Dispose()
        }
    }
}

if (-not $embeddedNoDialogs -and $startConfirmPrompt)
{
    $message = @(
        (Decode-Utf8Base64 "5Y2z5bCG5byA5aeLIENoYW5uZWwgU2V0dXDjgIFTaW5lIFNldHVwIOS4jiBOb3RjaCBQcm9maWxlcyDnmoToh6rliqjmipPlj5bjgII="),
        (Decode-Utf8Base64 "54K55Ye74oCc56Gu5a6a4oCd5ZCO77yM6ISa5pys5Lya5o6l566h6byg5qCH5bm25bCGIFRlc3RsYWIg5YiH5Yiw5YmN5Y+w44CC"),
        "",
        ((Decode-Utf8Base64 "Tm90Y2ggUHJvZmlsZSDluo/lj7c6IA==") + $notchProfileDisplay),
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
if ($NotchProfileCount -gt 0)
{
    Write-Host ("CHECKMIND_NOTCH_PROFILE_COUNT=" + $env:CHECKMIND_NOTCH_PROFILE_COUNT)
}
else
{
    Write-Host ("CHECKMIND_NOTCH_PROFILE_INDEXES=" + $env:CHECKMIND_NOTCH_PROFILE_INDEXES)
}
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

$businessRunStartTime = Get-Date
$latestRunBeforeStart = Get-LatestProbeRun $env:CHECKMIND_RUNS_ROOT

$appProcess = Start-Process -FilePath $appExe -PassThru
$appProcess.WaitForExit()
$exitCode = $appProcess.ExitCode
$handledCountMismatch = $false

$lastRun = Resolve-LastRun $env:CHECKMIND_RUNS_ROOT $businessRunStartTime $latestRunBeforeStart
if (![string]::IsNullOrWhiteSpace($lastRun))
{
    Write-Host ("last_run=" + $lastRun)
    $runResult = Get-RunResult $lastRun
    $countMismatch = $null
    if ($null -ne $runResult)
    {
        $countMismatch = $runResult.NotchProfileCountMismatch
    }

    if ($null -ne $countMismatch)
    {
        $handledCountMismatch = $true
        $requestedCount = 0
        $completedCount = 0
        $failedRowIndex = 0
        $userMessage = ""

        if ($null -ne $countMismatch.RequestedCount) { $requestedCount = [int]$countMismatch.RequestedCount }
        if ($null -ne $countMismatch.CompletedCount) { $completedCount = [int]$countMismatch.CompletedCount }
        if ($null -ne $countMismatch.FailedRowIndex) { $failedRowIndex = [int]$countMismatch.FailedRowIndex }
        if ($null -ne $countMismatch.UserMessage) { $userMessage = ($countMismatch.UserMessage + "") }

        Write-Host ("notch_profile_count_mismatch=requested:" + $requestedCount + ";completed:" + $completedCount + ";failedRow:" + $failedRowIndex)

        $mismatchMessage = @(
            (Decode-Utf8Base64 "Tm90Y2ggUHJvZmlsZSBDb3VudCDkuI7lrp7pmYXliJfooajmlbDph4/kuI3nrKbvvIE="),
            "",
            ((Decode-Utf8Base64 "55So5oi36K6+572u77ya") + " " + $requestedCount),
            ((Decode-Utf8Base64 "5a6e6ZmF5a6M5oiQ77ya") + " " + $completedCount),
            ((Decode-Utf8Base64 "57uI5q2i5bqP5Y+377ya") + " " + $failedRowIndex),
            "",
            ($userMessage),
            (Decode-Utf8Base64 "6K+35qOA5p+lIFNpbmUgU2V0dXAg55WM6Z2i5LiL55qEIE5vdGNoIFByb2ZpbGVzIOS4quaVsOiuvue9ruOAgg==")
        ) -join "`r`n"

        $mismatchCaption = (Decode-Utf8Base64 "Q2hlY2tNaW5kIC0gTm90Y2ggUHJvZmlsZSBDb3VudCDmj5DnpLo=")
        Write-Host "[CheckMind] Notch Profile Count mismatch dialog should be visible now."
        Show-TopMostAlert $mismatchMessage $mismatchCaption
    }

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

if ($exitCode -ne 0 -and -not $handledCountMismatch)
{
    exit $exitCode
}
