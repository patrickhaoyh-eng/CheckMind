param(
    [string]$NotchProfileIndexes = "",
    [int]$NotchProfileCount = 0,
    [switch]$NoPrompts
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Windows.Forms

$embeddedNoDialogs = $NoPrompts.IsPresent
$startConfirmPrompt = -not $embeddedNoDialogs

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
$mainRunEmbeddedNoDialogs = $embeddedNoDialogs
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

function Get-RunResultsSummary([string]$runDirectory)
{
    if ([string]::IsNullOrWhiteSpace($runDirectory))
    {
        return $null
    }

    $resultsPath = Join-Path $runDirectory "results.json"
    if (!(Test-Path $resultsPath))
    {
        return $null
    }

    try
    {
        return (Get-Content -LiteralPath $resultsPath -Raw | ConvertFrom-Json)
    }
    catch
    {
        return $null
    }
}

function New-FormalVerificationSummaryArtifact([string]$runDirectory, $results)
{
    $taskResult = $null
    $formalSummary = $null
    $alerts = @()
    if ($null -ne $results)
    {
        $taskResult = $results.taskResult
        $formalSummary = $results.formalVerificationSummary
        if ($null -ne $results.alerts)
        {
            foreach ($alert in @($results.alerts))
            {
                $alerts += [ordered]@{
                    severity = (($alert.severity) + "").Trim()
                    message = (($alert.message) + "").Trim()
                }
            }
        }
    }

    $failedObjects = @()
    $failedReasons = @()
    if ($null -ne $formalSummary)
    {
        if ($null -ne $formalSummary.failedObjects)
        {
            foreach ($failedObject in @($formalSummary.failedObjects))
            {
                $value = ($failedObject + "").Trim()
                if (-not [string]::IsNullOrWhiteSpace($value))
                {
                    $failedObjects += $value
                }
            }
        }

        if ($null -ne $formalSummary.failedReasons)
        {
            foreach ($failedReason in @($formalSummary.failedReasons))
            {
                $reasonCodes = @()
                if ($null -ne $failedReason.reasonCodes)
                {
                    foreach ($reasonCode in @($failedReason.reasonCodes))
                    {
                        $value = ($reasonCode + "").Trim()
                        if (-not [string]::IsNullOrWhiteSpace($value))
                        {
                            $reasonCodes += $value
                        }
                    }
                }

                $reasonMessages = @()
                if ($null -ne $failedReason.reasonMessages)
                {
                    foreach ($reasonMessage in @($failedReason.reasonMessages))
                    {
                        $value = ($reasonMessage + "").Trim()
                        if (-not [string]::IsNullOrWhiteSpace($value))
                        {
                            $reasonMessages += $value
                        }
                    }
                }

                $failedReasons += [ordered]@{
                    objectKey = ((($failedReason.objectKey) + "").Trim())
                    reasonCodes = $reasonCodes
                    reasonMessages = $reasonMessages
                }
            }
        }
    }

    $verificationStatus = "unavailable"
    if ($null -ne $formalSummary)
    {
        if ($formalSummary.allVerified)
        {
            $verificationStatus = "passed"
        }
        else
        {
            $verificationStatus = "failed"
        }
    }

    return [ordered]@{
        schemaVersion = 1
        runDirectory = $runDirectory
        runId = if ($null -ne $results) { (($results.runId) + "").Trim() } else { "" }
        taskResultStatus = if ($null -ne $taskResult) { (($taskResult.status) + "").Trim() } else { "" }
        requestedNotchProfileCount = if ($null -ne $taskResult) { $taskResult.requestedNotchProfileCount } else { $null }
        completedNotchProfileCount = if ($null -ne $taskResult) { $taskResult.completedNotchProfileCount } else { $null }
        taskResultMessage = if ($null -ne $taskResult) { ((($taskResult.message) + "").Trim()) } else { "" }
        formalVerificationStatus = $verificationStatus
        allVerified = if ($null -ne $formalSummary) { [bool]$formalSummary.allVerified } else { $null }
        failedObjects = $failedObjects
        failedReasons = $failedReasons
        finalCompareDirectory = if ($null -ne $formalSummary) { ((($formalSummary.finalCompareDirectory) + "").Trim()) } else { "" }
        alerts = $alerts
    }
}

function Write-FormalVerificationSummaryArtifact([string]$runDirectory, $results)
{
    if ([string]::IsNullOrWhiteSpace($runDirectory))
    {
        return $null
    }

    $artifactPath = Join-Path $runDirectory "formal_verification_summary.json"
    $artifact = New-FormalVerificationSummaryArtifact $runDirectory $results

    try
    {
        $artifact | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $artifactPath -Encoding UTF8
        Write-Host ("formal_verification_summary_file=" + $artifactPath)
        return $artifactPath
    }
    catch
    {
        Write-Host ("[CheckMind] failed to write formal verification summary file: " + $_.Exception.Message)
        return $null
    }
}

function Write-FormalVerificationSummary([string]$runDirectory, $results)
{
    Write-Host "[CheckMind] formal_verification_summary_begin"
    Write-Host ("run_directory=" + $runDirectory)

    if ($null -eq $results)
    {
        Write-Host "[CheckMind] results.json summary unavailable."
        Write-Host "[CheckMind] formal_verification_summary_end"
        return
    }

    $taskResult = $results.taskResult
    if ($null -ne $taskResult)
    {
        $status = ($taskResult.status + "").Trim()
        if (-not [string]::IsNullOrWhiteSpace($status))
        {
            Write-Host ("task_result_status=" + $status)
        }

        $requestedCount = ""
        if ($null -ne $taskResult.requestedNotchProfileCount)
        {
            $requestedCount = ($taskResult.requestedNotchProfileCount + "")
        }

        $completedCount = ""
        if ($null -ne $taskResult.completedNotchProfileCount)
        {
            $completedCount = ($taskResult.completedNotchProfileCount + "")
        }

        if (-not [string]::IsNullOrWhiteSpace($requestedCount) -or -not [string]::IsNullOrWhiteSpace($completedCount))
        {
            Write-Host ("notch_profile_progress=requested:" + $requestedCount + ";completed:" + $completedCount)
        }
    }

    $formalSummary = $results.formalVerificationSummary
    if ($null -eq $formalSummary)
    {
        Write-Host "[CheckMind] formal_verification_summary missing."
    }
    elseif ($formalSummary.allVerified)
    {
        Write-Host "[CheckMind] formal_verification=passed"
    }
    else
    {
        Write-Host "[CheckMind] formal_verification=failed"

        $failedObjects = @()
        if ($null -ne $formalSummary.failedObjects)
        {
            foreach ($failedObject in @($formalSummary.failedObjects))
            {
                $value = ($failedObject + "").Trim()
                if (-not [string]::IsNullOrWhiteSpace($value))
                {
                    $failedObjects += $value
                }
            }
        }

        if ($failedObjects.Count -gt 0)
        {
            Write-Host ("failed_objects=" + ($failedObjects -join ","))
        }

        if ($null -ne $formalSummary.failedReasons)
        {
            foreach ($failedReason in @($formalSummary.failedReasons))
            {
                $objectKey = (($failedReason.objectKey) + "").Trim()
                $reasonMessages = @()
                if ($null -ne $failedReason.reasonMessages)
                {
                    foreach ($reasonMessage in @($failedReason.reasonMessages))
                    {
                        $value = ($reasonMessage + "").Trim()
                        if (-not [string]::IsNullOrWhiteSpace($value))
                        {
                            $reasonMessages += $value
                        }
                    }
                }

                if ($reasonMessages.Count -gt 0)
                {
                    foreach ($reasonMessage in $reasonMessages)
                    {
                        Write-Host ("reason_message[" + $objectKey + "]=" + $reasonMessage)
                    }
                }
                elseif ($null -ne $failedReason.reasonCodes)
                {
                    $reasonCodes = @()
                    foreach ($reasonCode in @($failedReason.reasonCodes))
                    {
                        $value = ($reasonCode + "").Trim()
                        if (-not [string]::IsNullOrWhiteSpace($value))
                        {
                            $reasonCodes += $value
                        }
                    }

                    if ($reasonCodes.Count -gt 0)
                    {
                        Write-Host ("reason_codes[" + $objectKey + "]=" + ($reasonCodes -join ","))
                    }
                }
            }
        }
    }

    if ($null -ne $taskResult)
    {
        $taskResultMessage = (($taskResult.message) + "").Trim()
        if (-not [string]::IsNullOrWhiteSpace($taskResultMessage))
        {
            Write-Host ("task_result_message=" + $taskResultMessage)
        }
    }

    if ($null -ne $results.alerts)
    {
        foreach ($alert in @($results.alerts))
        {
            $alertSeverity = (($alert.severity) + "").Trim()
            $alertMessage = (($alert.message) + "").Trim()
            if (-not [string]::IsNullOrWhiteSpace($alertMessage))
            {
                Write-Host ("alert[" + $alertSeverity + "]=" + $alertMessage)
            }
        }
    }

    if ($null -ne $formalSummary)
    {
        $finalCompareDirectory = (($formalSummary.finalCompareDirectory) + "").Trim()
        if (-not [string]::IsNullOrWhiteSpace($finalCompareDirectory))
        {
            Write-Host ("final_compare_directory=" + $finalCompareDirectory)
        }
    }

    Write-Host "[CheckMind] formal_verification_summary_end"
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

function Set-DialogMode([bool]$suppressDialogs, [bool]$consentPromptEnabled, [bool]$finishedPromptEnabled)
{
    if ($suppressDialogs)
    {
        $env:CHECKMIND_CAPTURE_PROMPT = "0"
        $env:CHECKMIND_CAPTURE_CONSENT_PROMPT = "0"
        $env:CHECKMIND_CAPTURE_FINISHED_PROMPT = "0"
        $env:CHECKMIND_EMBEDDED_NO_DIALOGS = "1"
        return
    }

    $env:CHECKMIND_CAPTURE_PROMPT = "1"
    $env:CHECKMIND_CAPTURE_CONSENT_PROMPT = $(if ($consentPromptEnabled) { "1" } else { "0" })
    $env:CHECKMIND_CAPTURE_FINISHED_PROMPT = $(if ($finishedPromptEnabled) { "1" } else { "0" })
    Remove-Item Env:CHECKMIND_EMBEDDED_NO_DIALOGS -ErrorAction SilentlyContinue
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

if ($autoRecalibrateVerify)
{
    Set-DialogMode $true $false $false
    Write-Host "Preflight: recalibrate verify signature (reuse ClickPoint)"
    powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\calibrate_verify_signature_reuse.ps1")
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

Set-DialogMode $mainRunEmbeddedNoDialogs $false $true

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
Write-Host ("CHECKMIND_CAPTURE_CONSENT_PROMPT=" + $env:CHECKMIND_CAPTURE_CONSENT_PROMPT)
Write-Host ("CHECKMIND_CAPTURE_FINISHED_PROMPT=" + $env:CHECKMIND_CAPTURE_FINISHED_PROMPT)
Write-Host ("CHECKMIND_EMBEDDED_NO_DIALOGS=" + (($env:CHECKMIND_EMBEDDED_NO_DIALOGS) + ""))
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
    $resultsSummary = Get-RunResultsSummary $lastRun
    $null = Write-FormalVerificationSummaryArtifact $lastRun $resultsSummary
    Write-FormalVerificationSummary $lastRun $resultsSummary
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
        if ($embeddedNoDialogs)
        {
            Write-Host "[CheckMind] mismatch dialog suppressed because -NoPrompts was specified."
        }
        else
        {
            Write-Host "[CheckMind] Notch Profile Count mismatch dialog should be visible now."
            Show-TopMostAlert $mismatchMessage $mismatchCaption
        }
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
