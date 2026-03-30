Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot "SleekPicker.Setup\SleekPicker.Setup.csproj"
$versionPath = Join-Path $repoRoot "version.txt"
$outputDir = Join-Path $repoRoot "SleekPicker.Setup\publish"
$setupExePath = Join-Path $repoRoot "setup.exe"

if (-not (Test-Path $projectPath))
{
    throw "Could not find setup launcher project at '$projectPath'."
}

if (-not (Test-Path $versionPath))
{
    throw "Could not find version.txt at '$versionPath'."
}

$version = (Get-Content -Path $versionPath -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($version))
{
    throw "version.txt is empty."
}

if (Test-Path $outputDir)
{
    Remove-Item -Path $outputDir -Recurse -Force
}

$publishArgs = @(
    "publish"
    $projectPath
    "-c"
    "Release"
    "-p:DebugType=None"
    "-p:DebugSymbols=false"
    "-o"
    $outputDir
)

dotnet @publishArgs
if ($LASTEXITCODE -ne 0)
{
    throw "Failed to publish setup launcher."
}

$publishedExe = Join-Path $outputDir "SleekPicker.Setup.exe"
if (-not (Test-Path $publishedExe))
{
    throw "Expected published launcher not found: $publishedExe"
}

Copy-Item -Path $publishedExe -Destination $setupExePath -Force

Write-Host "Setup launcher version $version published to: $setupExePath"
Write-Host "setup.exe is a native installer UI and does not require launching powershell.exe."
Write-Host "This build targets .NET Framework 4.8 (included with Windows 11)."
