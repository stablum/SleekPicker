Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-RelativePath([string] $basePath, [string] $targetPath)
{
    $normalizedBase = [System.IO.Path]::GetFullPath($basePath)
    if (-not $normalizedBase.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString()))
    {
        $normalizedBase += [System.IO.Path]::DirectorySeparatorChar
    }

    $normalizedTarget = [System.IO.Path]::GetFullPath($targetPath)
    $baseUri = New-Object System.Uri($normalizedBase)
    $targetUri = New-Object System.Uri($normalizedTarget)
    $relative = $baseUri.MakeRelativeUri($targetUri)
    return [System.Uri]::UnescapeDataString($relative.ToString()).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Test-IncludedPath([string] $relativePath)
{
    $path = $relativePath.Replace('/', '\')

    if ([string]::IsNullOrWhiteSpace($path))
    {
        return $false
    }

    if ($path -like "*.zip")
    {
        return $false
    }

    if ($path -like "*.un~" -or $path -like "*~")
    {
        return $false
    }

    if ($path -like ".git\*" -or $path -like ".vs\*")
    {
        return $false
    }

    if ($path -like "*\obj\*" -or $path -like "obj\*")
    {
        return $false
    }

    if ($path -like "SleekPicker.Setup\publish\*" -or $path -like "SleekPicker.Setup\bin\*")
    {
        return $false
    }

    if ($path -like "SleekPicker.App\bin\Debug\*")
    {
        return $false
    }

    return $true
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$versionPath = Join-Path $repoRoot "version.txt"
$setupBuilderPath = Join-Path $repoRoot "build-setup-exe.ps1"

if (-not (Test-Path $versionPath))
{
    throw "version.txt not found at '$versionPath'."
}

$version = (Get-Content -Path $versionPath -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($version))
{
    throw "version.txt is empty."
}

$archiveName = "SleekPicker-$version.zip"
$archivePath = Join-Path $repoRoot $archiveName

if (Test-Path $setupBuilderPath)
{
    & $setupBuilderPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to build setup.exe."
    }
}

dotnet build (Join-Path $repoRoot "SleekPicker.slnx") -c Release
if ($LASTEXITCODE -ne 0)
{
    throw "Failed to build solution before packaging."
}

$files = @(
    Get-ChildItem -Path $repoRoot -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            if ($_.FullName -eq $archivePath)
            {
                return $false
            }

            $relativePath = Get-RelativePath -basePath $repoRoot -targetPath $_.FullName
            return (Test-IncludedPath -relativePath $relativePath)
        }
)

if ($files.Count -eq 0)
{
    throw "No files found to archive."
}

if (Test-Path $archivePath)
{
    Remove-Item -Path $archivePath -Force
}

$archive = [System.IO.Compression.ZipFile]::Open($archivePath, [System.IO.Compression.ZipArchiveMode]::Create)
try
{
    foreach ($file in $files)
    {
        $relativePath = Get-RelativePath -basePath $repoRoot -targetPath $file.FullName
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $file.FullName,
            $relativePath,
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally
{
    $archive.Dispose()
}

Write-Host "Created archive: $archivePath"
Write-Host "Files added: $($files.Count)"
