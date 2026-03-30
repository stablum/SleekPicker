param(
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionPath = Join-Path $repoRoot "SleekPicker.slnx"
$appExePath = Join-Path $repoRoot "SleekPicker.App\bin\Debug\net8.0-windows10.0.19041.0\SleekPicker.App.exe"
$appDllPath = Join-Path $repoRoot "SleekPicker.App\bin\Debug\net8.0-windows10.0.19041.0\SleekPicker.App.dll"

if ($Rebuild -or -not (Test-Path $appDllPath))
{
    Write-Host "Building SleekPicker..."
    dotnet build $solutionPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Build failed, cannot restart SleekPicker."
    }
}

# Stop direct app processes.
$runningApp = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "SleekPicker*" }
if ($runningApp)
{
    $runningApp | Stop-Process -Force
}

# Stop dotnet host processes that run SleekPicker.
$runningHosted = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -ieq "dotnet.exe" -and
        $_.CommandLine -like "*SleekPicker.App.dll*"
    }

if ($runningHosted)
{
    $runningHosted | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
}

try
{
    $started = Start-Process -FilePath "dotnet" -ArgumentList @($appDllPath) -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru
}
catch
{
    throw "Failed to start SleekPicker: $($_.Exception.Message)"
}

if ($started)
{
    Write-Host "SleekPicker restarted via hidden dotnet host. PID: $($started.Id)"
}
else
{
    throw "SleekPicker did not start."
}
