param(
    [string]$RunsRoot = "",
    [string]$OutputDirectory = "",
    [string]$TaskType = "",
    [int]$Limit = 0
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RunsRoot))
{
    $RunsRoot = Join-Path $repoRoot "artifacts\probe-runs"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $RunsRoot "_reports"
}

function Read-JsonFile([string]$path)
{
    if ([string]::IsNullOrWhiteSpace($path) -or !(Test-Path -LiteralPath $path))
    {
        return $null
    }

    try
    {
        return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
    }
    catch
    {
        return $null
    }
}

function Get-JoinedText($values, [string]$separator = ",")
{
    if ($null -eq $values)
    {
        return ""
    }

    $items = @()
    foreach ($value in @($values))
    {
        $text = ($value + "").Trim()
        if (-not [string]::IsNullOrWhiteSpace($text))
        {
            $items += $text
        }
    }

    if ($items.Count -eq 0)
    {
        return ""
    }

    return ($items -join $separator)
}

function Get-ReasonMessagesText($failedReasons)
{
    if ($null -eq $failedReasons)
    {
        return ""
    }

    $messages = @()
    foreach ($failedReason in @($failedReasons))
    {
        if ($null -eq $failedReason.reasonMessages)
        {
            continue
        }

        foreach ($reasonMessage in @($failedReason.reasonMessages))
        {
            $text = ($reasonMessage + "").Trim()
            if (-not [string]::IsNullOrWhiteSpace($text))
            {
                $messages += $text
            }
        }
    }

    if ($messages.Count -eq 0)
    {
        return ""
    }

    return ($messages -join " | ")
}

function Get-MarkdownCell([string]$text)
{
    if ([string]::IsNullOrWhiteSpace($text))
    {
        return ""
    }

    return (($text -replace "\r?\n", "<br/>") -replace "\|", "\|")
}

function Get-OutputFileStem([string]$TaskType, [int]$Limit)
{
    $segments = @("formal_verification_batch_summary")

    $taskTypeValue = ($TaskType + "").Trim()
    if (-not [string]::IsNullOrWhiteSpace($taskTypeValue))
    {
        $sanitizedTaskType = (($taskTypeValue -replace "[^A-Za-z0-9_-]", "_").Trim("_")).ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace($sanitizedTaskType))
        {
            $sanitizedTaskType = "filtered"
        }

        $segments += ("tasktype_" + $sanitizedTaskType)
    }

    if ($Limit -gt 0)
    {
        $segments += ("latest_" + $Limit)
    }

    return ($segments -join "_")
}

if (!(Test-Path -LiteralPath $RunsRoot))
{
    throw "Runs root not found: $RunsRoot"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$entries = @()
$runDirectories = Get-ChildItem -LiteralPath $RunsRoot -Directory |
    Where-Object { $_.Name -notlike "_*" } |
    Sort-Object LastWriteTimeUtc -Descending

foreach ($runDirectory in $runDirectories)
{
    $summaryPath = Join-Path $runDirectory.FullName "formal_verification_summary.json"
    if (!(Test-Path -LiteralPath $summaryPath))
    {
        continue
    }

    $summary = Read-JsonFile $summaryPath
    if ($null -eq $summary)
    {
        continue
    }

    $metaPath = Join-Path $runDirectory.FullName "meta.json"
    $meta = Read-JsonFile $metaPath
    $taskRequest = $null
    if ($null -ne $meta)
    {
        $taskRequest = $meta.TaskRequest
    }

    $failedObjectsText = Get-JoinedText $summary.failedObjects ","
    $reasonMessagesText = Get-ReasonMessagesText $summary.failedReasons
    $taskTypeText = if ($null -ne $taskRequest) { (($taskRequest.taskType) + "").Trim() } else { "" }

    $taskTypeFilter = ($TaskType + "").Trim()
    if (-not [string]::IsNullOrWhiteSpace($taskTypeFilter) -and
        -not [string]::Equals($taskTypeText, $taskTypeFilter, [System.StringComparison]::OrdinalIgnoreCase))
    {
        continue
    }

    $entry = [pscustomobject]@{
        runId = (($summary.runId) + "").Trim()
        createdAtUtc = if ($null -ne $meta) { (($meta.CreatedAtUtc) + "").Trim() } else { "" }
        taskType = $taskTypeText
        notchProfileCount = if ($null -ne $taskRequest) { $taskRequest.notchProfileCount } else { $null }
        taskResultStatus = (($summary.taskResultStatus) + "").Trim()
        formalVerificationStatus = (($summary.formalVerificationStatus) + "").Trim()
        allVerified = $summary.allVerified
        failedObjects = @($summary.failedObjects)
        failedObjectsText = $failedObjectsText
        reasonMessagesText = $reasonMessagesText
        finalCompareDirectory = (($summary.finalCompareDirectory) + "").Trim()
        runDirectory = $runDirectory.FullName
        summaryFilePath = $summaryPath
    }

    $entries += $entry
}

if ($Limit -gt 0 -and $entries.Count -gt $Limit)
{
    $entries = @($entries | Select-Object -First $Limit)
}

$outputFileStem = Get-OutputFileStem $TaskType $Limit
$jsonReportPath = Join-Path $OutputDirectory ($outputFileStem + ".json")
$markdownReportPath = Join-Path $OutputDirectory ($outputFileStem + ".md")

$report = [pscustomobject]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    runsRoot = $RunsRoot
    taskTypeFilter = (($TaskType) + "").Trim()
    limit = if ($Limit -gt 0) { $Limit } else { $null }
    totalRuns = $entries.Count
    passedRuns = @($entries | Where-Object { $_.formalVerificationStatus -eq "passed" }).Count
    failedRuns = @($entries | Where-Object { $_.formalVerificationStatus -eq "failed" }).Count
    runs = @($entries)
}

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonReportPath -Encoding UTF8

$markdownLines = @()
$taskTypeFilterText = "<all>"
if (-not [string]::IsNullOrWhiteSpace($report.taskTypeFilter))
{
    $taskTypeFilterText = $report.taskTypeFilter
}

$limitText = "<all>"
if ($null -ne $report.limit)
{
    $limitText = ($report.limit + "")
}

$markdownLines += "# Formal Verification Batch Summary"
$markdownLines += ""
$markdownLines += "- generatedAtUtc: " + $report.generatedAtUtc
$markdownLines += "- runsRoot: " + $RunsRoot
$markdownLines += "- taskTypeFilter: " + $taskTypeFilterText
$markdownLines += "- limit: " + $limitText
$markdownLines += "- totalRuns: " + $report.totalRuns
$markdownLines += "- passedRuns: " + $report.passedRuns
$markdownLines += "- failedRuns: " + $report.failedRuns
$markdownLines += ""
$markdownLines += "| RunId | CreatedAtUtc | TaskType | NotchProfileCount | TaskResultStatus | FormalVerificationStatus | FailedObjects | ReasonMessages |"
$markdownLines += "| --- | --- | --- | --- | --- | --- | --- | --- |"

foreach ($entry in $entries)
{
    $markdownLines += "| " +
        (Get-MarkdownCell $entry.runId) + " | " +
        (Get-MarkdownCell $entry.createdAtUtc) + " | " +
        (Get-MarkdownCell $entry.taskType) + " | " +
        (Get-MarkdownCell ($entry.notchProfileCount + "")) + " | " +
        (Get-MarkdownCell $entry.taskResultStatus) + " | " +
        (Get-MarkdownCell $entry.formalVerificationStatus) + " | " +
        (Get-MarkdownCell $entry.failedObjectsText) + " | " +
        (Get-MarkdownCell $entry.reasonMessagesText) + " |"
}

$markdownLines | Set-Content -LiteralPath $markdownReportPath -Encoding UTF8

Write-Host ("formal_verification_batch_summary_json=" + $jsonReportPath)
Write-Host ("formal_verification_batch_summary_md=" + $markdownReportPath)
Write-Host ("total_runs=" + $report.totalRuns)
Write-Host ("passed_runs=" + $report.passedRuns)
Write-Host ("failed_runs=" + $report.failedRuns)
