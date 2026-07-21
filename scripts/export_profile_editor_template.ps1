param(
    [string]$TemplatePath = "",
    [string]$RunsRoot = "",
    [string]$RunId = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RunsRoot))
{
    $RunsRoot = Join-Path $repoRoot "artifacts\probe-runs"
}

$resolvedTemplatePath = ""
if (-not [string]::IsNullOrWhiteSpace($TemplatePath))
{
    $resolvedTemplatePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($TemplatePath.Trim())
}
else
{
    $templateDirectory = Join-Path $repoRoot "docs"
    $templateFile = Get-ChildItem -LiteralPath $templateDirectory -File |
        Where-Object { $_.Name -like "15-Profile Editor*.json" } |
        Select-Object -First 1

    if ($null -ne $templateFile)
    {
        $resolvedTemplatePath = $templateFile.FullName
    }
}

$projectPath = Join-Path $repoRoot "CheckMind.App\CheckMind.App.csproj"
if (!(Test-Path -LiteralPath $resolvedTemplatePath))
{
    throw "Template json not found: $resolvedTemplatePath"
}

if (!(Test-Path -LiteralPath $projectPath))
{
    throw "Project file not found: $projectPath"
}

New-Item -ItemType Directory -Force -Path $RunsRoot | Out-Null

$probeRoot = Join-Path $env:TEMP "checkmind-profile-editor-template-exporter"
New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null

$probeProjectPath = Join-Path $probeRoot "checkmind-profile-editor-template-exporter.csproj"
$probeProgramPath = Join-Path $probeRoot "Program.cs"

$probeProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows7.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$projectPath" />
  </ItemGroup>
</Project>
"@

$probeProgram = @"
using CheckMind.App.Core;

if (args.Length < 2)
{
    throw new InvalidOperationException("Expected arguments: <templatePath> <runsRoot> [runId]");
}

var templatePath = args[0];
var runsRoot = args[1];
var runId = args.Length >= 3 ? args[2] : string.Empty;

var storage = new RunStorage(runsRoot);
var options = string.IsNullOrWhiteSpace(runId)
    ? new RunCreateOptions(TrialId: "PROFILE_EDITOR_TEMPLATE_EXPORT")
    : new RunCreateOptions(TrialId: "PROFILE_EDITOR_TEMPLATE_EXPORT", RunId: runId);

var run = storage.CreateRun(options);
var store = new ProfileEditorExtractionStore();
var result = store.Load(templatePath);
var outputPath = store.SaveToRun(run, result);

Console.WriteLine($"template_input={templatePath}");
Console.WriteLine($"run_id={run.RunId}");
Console.WriteLine($"run_directory={run.RunDirectory}");
Console.WriteLine($"output_path={outputPath}");
Console.WriteLine($"default_file_name={ProfileEditorExtractionStore.DefaultFileName}");
Console.WriteLine($"object_key={result.ObjectKey}");
Console.WriteLine($"mapping_status={result.MappingStatus}");
Console.WriteLine($"row_count={result.Rows.Count}");
Console.WriteLine($"cell_count={result.Rows[0].Cells.Count}");
"@

Set-Content -LiteralPath $probeProjectPath -Value $probeProject -Encoding UTF8
Set-Content -LiteralPath $probeProgramPath -Value $probeProgram -Encoding UTF8

$arguments = @(
    "run",
    "--project", $probeProjectPath,
    "--",
    $resolvedTemplatePath,
    $RunsRoot
)

if (-not [string]::IsNullOrWhiteSpace($RunId))
{
    $sanitizedRunId = $RunId.Trim()
    if ($sanitizedRunId -notmatch '^[A-Za-z0-9_-]+$')
    {
        throw "RunId contains unsupported characters: $sanitizedRunId"
    }

    $arguments += $sanitizedRunId
}

Write-Host ("template_path=" + $resolvedTemplatePath)
if (-not [string]::IsNullOrWhiteSpace($RunId))
{
    Write-Host ("requested_run_id=" + $RunId.Trim())
}

& dotnet @arguments
