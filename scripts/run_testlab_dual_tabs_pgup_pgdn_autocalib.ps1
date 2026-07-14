$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

Add-Type -AssemblyName PresentationFramework

$embeddedNoDialogs = (($env:CHECKMIND_EMBEDDED_NO_DIALOGS) + "").Trim()
$embeddedNoDialogs = $embeddedNoDialogs -eq "1" -or $embeddedNoDialogs -eq "true"

function Decode-Utf8Base64([string]$value)
{
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($value))
}

$message = @(
    (Decode-Utf8Base64 "5Y2z5bCG5byA5aeL5Y+M5q2l6aqk6Ieq5Yqo5rWB56iL77ya"),
    (Decode-Utf8Base64 "MS4g5aSN55So546w5pyJIENsaWNrUG9pbnQg6YeN5qCH5a6a6aqM55yf562+5ZCN"),
    (Decode-Utf8Base64 "Mi4g6L+Q6KGMIENoYW5uZWwgU2V0dXAgKyBTaW5lIFNldHVwIOWPjOmhteWIh+mhteS4jue/u+mhtea1i+ivlQ=="),
    "",
    (Decode-Utf8Base64 "5rWB56iL5byA5aeL5ZCO5Lya5o6l566h6byg5qCHL+mUruebmOOAgg=="),
    (Decode-Utf8Base64 "54K55Ye756Gu5a6a5ZCO56uL5Y2z5byA5aeL77yM5Lit6YCU5LiN5YaN5by55byA5aeL56Gu6K6k5qGG44CC")
) -join "`r`n"

if (-not $embeddedNoDialogs)
{
    $result = [System.Windows.MessageBox]::Show(
        $message,
        (Decode-Utf8Base64 "Q2hlY2tNaW5kIC0g5byA5aeL6Ieq5Yqo6YeN5qCH5a6a5bm25rWL6K+V"),
        [System.Windows.MessageBoxButton]::OKCancel,
        [System.Windows.MessageBoxImage]::Warning
    )

    if ($result -ne [System.Windows.MessageBoxResult]::OK)
    {
        Write-Host (Decode-Utf8Base64 "5bey5Y+W5raI44CC")
        exit 0
    }
}

Write-Host "Step 1/2: Recalibrate verify signature (reuse ClickPoint)"
$env:CHECKMIND_CAPTURE_PROMPT = "0"
powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\calibrate_verify_signature_reuse.ps1")
if ($LASTEXITCODE -ne 0)
{
    [System.Windows.MessageBox]::Show(
        (Decode-Utf8Base64 "56ysIDEg5q2l5aSx6LSl77yM5bey5YGc5q2i77yM5pyq57un57ut5omn6KGM56ysIDIg5q2l44CC"),
        (Decode-Utf8Base64 "Q2hlY2tNaW5kIC0g5byA5aeL6Ieq5Yqo6YeN5qCH5a6a5bm25rWL6K+V"),
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Error
    ) | Out-Null
    exit $LASTEXITCODE
}

Write-Host "Step 2/2: Run dual-tabs capture (PgUp/PgDn)"
$env:CHECKMIND_CAPTURE_PROMPT = "0"
powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\run_testlab_dual_tabs_pgup_pgdn.ps1")
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$lastRunPath = Join-Path $env:TEMP "checkmind_testlab_last_run.txt"
$lastRun = ""
if (Test-Path $lastRunPath)
{
    $lastRun = (Get-Content -Raw $lastRunPath).Trim()
}

$evidenceDir = if (![string]::IsNullOrWhiteSpace($lastRun)) { Join-Path $lastRun "screenshots\\evidence" } else { "" }
$openTarget = if (![string]::IsNullOrWhiteSpace($evidenceDir) -and (Test-Path $evidenceDir)) { $evidenceDir } else { $lastRun }
if (-not $embeddedNoDialogs -and -not [string]::IsNullOrWhiteSpace($openTarget) -and (Test-Path $openTarget))
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

if (-not $embeddedNoDialogs)
{
    [System.Windows.MessageBox]::Show(
        $finishMessage,
        (Decode-Utf8Base64 "Q2hlY2tNaW5kIC0g6Ieq5Yqo5rWB56iL5a6M5oiQ"),
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Information
    ) | Out-Null
}
