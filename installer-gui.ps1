Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([System.Threading.Thread]::CurrentThread.ApartmentState -ne [System.Threading.ApartmentState]::STA)
{
    $scriptPath = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($scriptPath))
    {
        $scriptPath = $MyInvocation.MyCommand.Path
    }

    $hostPath = (Get-Process -Id $PID).Path
    if ([string]::IsNullOrWhiteSpace($hostPath) -or -not (Test-Path $hostPath))
    {
        $hostPath = Join-Path $PSHOME "powershell.exe"
    }

    $arguments = @(
        "-NoProfile"
        "-ExecutionPolicy"
        "Bypass"
        "-STA"
        "-File"
        "`"$scriptPath`""
    )

    Start-Process -FilePath $hostPath -ArgumentList $arguments | Out-Null
    return
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$versionFilePath = Join-Path $repoRoot "version.txt"
$solutionPath = Join-Path $repoRoot "SleekPicker.slnx"
$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$entryName = "SleekPicker"

function Get-VersionLabel
{
    if (-not (Test-Path $versionFilePath))
    {
        return "unknown"
    }

    $value = (Get-Content $versionFilePath -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($value))
    {
        return "unknown"
    }

    return $value
}

function Resolve-AppExecutablePath
{
    $binPath = Join-Path $repoRoot "SleekPicker.App\bin"
    if (-not (Test-Path $binPath))
    {
        return $null
    }

    $releaseExecutables = @(
        Get-ChildItem -Path $binPath -Filter "SleekPicker.App.exe" -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\Release\*" } |
            Sort-Object LastWriteTimeUtc -Descending
    )

    if ($releaseExecutables.Count -eq 0)
    {
        return $null
    }

    return $releaseExecutables[0].FullName
}

function Ensure-AppExecutable
{
    $existing = Resolve-AppExecutablePath
    if ($null -ne $existing -and (Test-Path $existing))
    {
        return $existing
    }

    if (-not (Test-Path $solutionPath))
    {
        throw "Cannot locate solution file: $solutionPath"
    }

    dotnet build $solutionPath -c Release
    if ($LASTEXITCODE -ne 0)
    {
        throw "Build failed. Autorun was not installed."
    }

    $built = Resolve-AppExecutablePath
    if (-not $built)
    {
        throw "Build succeeded but SleekPicker.App.exe was not found in Release output."
    }

    return $built
}

function Get-RunningSleekPickerProcess([string] $exePath)
{
    if ([string]::IsNullOrWhiteSpace($exePath))
    {
        return $null
    }

    $normalizedTarget = [System.IO.Path]::GetFullPath($exePath)
    $matches = @(
        Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -ieq "SleekPicker.App.exe" -and
                -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
                [string]::Equals(
                    [System.IO.Path]::GetFullPath($_.ExecutablePath),
                    $normalizedTarget,
                    [System.StringComparison]::OrdinalIgnoreCase)
            }
    )

    if ($matches.Count -eq 0)
    {
        return $null
    }

    return $matches[0]
}

function Start-SleekPickerNow
{
    $exePath = Ensure-AppExecutable
    $alreadyRunning = Get-RunningSleekPickerProcess -exePath $exePath
    if ($alreadyRunning)
    {
        return @{
            AlreadyRunning = $true
            ProcessId = $alreadyRunning.ProcessId
            ExePath = $exePath
        }
    }

    $started = Start-Process -FilePath $exePath -WorkingDirectory (Split-Path -Parent $exePath) -PassThru
    if ($null -eq $started)
    {
        throw "SleekPicker did not start."
    }

    return @{
        AlreadyRunning = $false
        ProcessId = $started.Id
        ExePath = $exePath
    }
}

function Get-RunningSleekPickerProcessIds
{
    $directIds = @(
        Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.ProcessName -like "SleekPicker*" } |
            Select-Object -ExpandProperty Id
    )

    $hostedIds = @(
        Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -ieq "dotnet.exe" -and
                $_.CommandLine -like "*SleekPicker.App.dll*"
            } |
            Select-Object -ExpandProperty ProcessId
    )

    return @($directIds + $hostedIds | Sort-Object -Unique)
}

function Stop-SleekPickerNow
{
    $runningIds = @(Get-RunningSleekPickerProcessIds)
    $stoppedIds = New-Object 'System.Collections.Generic.List[int]'

    foreach ($id in $runningIds)
    {
        try
        {
            Stop-Process -Id $id -Force -ErrorAction Stop
            $stoppedIds.Add([int]$id)
        }
        catch
        {
            # Ignore races where process exits between detection and stop.
        }
    }

    return @{
        RunningCount = $runningIds.Count
        StoppedCount = $stoppedIds.Count
        StoppedIds = @($stoppedIds)
    }
}

function Test-DotNet8RuntimeInstalled
{
    try
    {
        $runtimes = @(dotnet --list-runtimes 2>$null)
        return [bool]($runtimes | Where-Object { $_ -match "^Microsoft\.NETCore\.App 8\." })
    }
    catch
    {
        return $false
    }
}

function Get-DotNetSdkStatus
{
    try
    {
        $sdks = @(dotnet --list-sdks 2>$null)
        $parsedVersions = @()

        foreach ($line in $sdks)
        {
            $token = ($line -split "\s+")[0]
            if ([string]::IsNullOrWhiteSpace($token))
            {
                continue
            }

            $normalized = ($token -split "-")[0]
            try
            {
                $parsedVersions += [Version]$normalized
            }
            catch
            {
                # Ignore unparseable SDK lines.
            }
        }

        if ($parsedVersions.Count -eq 0)
        {
            return @{
                IsInstalled = $false
                Display = "Missing"
            }
        }

        $highest = $parsedVersions | Sort-Object -Descending | Select-Object -First 1
        $isAtLeast8 = $highest.Major -ge 8
        if ($isAtLeast8)
        {
            return @{
                IsInstalled = $true
                Display = "Installed ($highest)"
            }
        }

        return @{
            IsInstalled = $false
            Display = "Missing (found $highest)"
        }
    }
    catch
    {
        return @{
            IsInstalled = $false
            Display = "Missing"
        }
    }
}

function Install-WingetPackage([string] $id, [string] $name)
{
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($null -eq $winget)
    {
        throw "winget is not available on this machine. Install App Installer from Microsoft Store first."
    }

    $arguments = @(
        "install"
        "--id"
        $id
        "--exact"
        "--source"
        "winget"
        "--accept-source-agreements"
        "--accept-package-agreements"
        "--silent"
        "--disable-interactivity"
    )

    $output = & $winget.Source @arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and $exitCode -ne 3010)
    {
        $details = ($output | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($details))
        {
            $details = "No additional output."
        }

        throw "$name installation failed (exit code $exitCode).`r`n$details"
    }

    return @{
        ExitCode = $exitCode
        Output = ($output | Out-String)
    }
}

function Get-InstalledAutorunCommand
{
    try
    {
        $value = Get-ItemPropertyValue -Path $runKeyPath -Name $entryName -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($value))
        {
            return $null
        }

        return $value
    }
    catch
    {
        return $null
    }
}

function Set-AutorunCommand([string] $command)
{
    New-Item -Path $runKeyPath -Force | Out-Null
    Set-ItemProperty -Path $runKeyPath -Name $entryName -Value $command
}

function Remove-AutorunCommand
{
    Remove-ItemProperty -Path $runKeyPath -Name $entryName -ErrorAction SilentlyContinue
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "SleekPicker Installer"
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$form.Size = New-Object System.Drawing.Size(780, 390)
$form.MinimumSize = New-Object System.Drawing.Size(780, 390)
$form.MaximizeBox = $false
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog

$titleLabel = New-Object System.Windows.Forms.Label
$titleLabel.Text = "SleekPicker Autorun Setup"
$titleLabel.Location = New-Object System.Drawing.Point(20, 18)
$titleLabel.Size = New-Object System.Drawing.Size(420, 28)
$titleLabel.Font = New-Object System.Drawing.Font("Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($titleLabel)

$versionCaptionLabel = New-Object System.Windows.Forms.Label
$versionCaptionLabel.Text = "Version:"
$versionCaptionLabel.Location = New-Object System.Drawing.Point(20, 58)
$versionCaptionLabel.Size = New-Object System.Drawing.Size(70, 24)
$versionCaptionLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$form.Controls.Add($versionCaptionLabel)

$versionValueLabel = New-Object System.Windows.Forms.Label
$versionValueLabel.Text = (Get-VersionLabel)
$versionValueLabel.Location = New-Object System.Drawing.Point(90, 58)
$versionValueLabel.Size = New-Object System.Drawing.Size(200, 24)
$versionValueLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($versionValueLabel)

$statusCaptionLabel = New-Object System.Windows.Forms.Label
$statusCaptionLabel.Text = "Autorun:"
$statusCaptionLabel.Location = New-Object System.Drawing.Point(20, 85)
$statusCaptionLabel.Size = New-Object System.Drawing.Size(70, 24)
$statusCaptionLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$form.Controls.Add($statusCaptionLabel)

$statusValueLabel = New-Object System.Windows.Forms.Label
$statusValueLabel.Location = New-Object System.Drawing.Point(90, 85)
$statusValueLabel.Size = New-Object System.Drawing.Size(240, 24)
$statusValueLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($statusValueLabel)

$statusDetailsLabel = New-Object System.Windows.Forms.Label
$statusDetailsLabel.Location = New-Object System.Drawing.Point(20, 112)
$statusDetailsLabel.Size = New-Object System.Drawing.Size(730, 24)
$statusDetailsLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$form.Controls.Add($statusDetailsLabel)

$commandCaptionLabel = New-Object System.Windows.Forms.Label
$commandCaptionLabel.Text = "Current entry:"
$commandCaptionLabel.Location = New-Object System.Drawing.Point(20, 142)
$commandCaptionLabel.Size = New-Object System.Drawing.Size(90, 24)
$commandCaptionLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$form.Controls.Add($commandCaptionLabel)

$commandValueBox = New-Object System.Windows.Forms.TextBox
$commandValueBox.Location = New-Object System.Drawing.Point(112, 140)
$commandValueBox.Size = New-Object System.Drawing.Size(638, 24)
$commandValueBox.ReadOnly = $true
$commandValueBox.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$form.Controls.Add($commandValueBox)

$runtimeCaptionLabel = New-Object System.Windows.Forms.Label
$runtimeCaptionLabel.Text = ".NET 8 Runtime:"
$runtimeCaptionLabel.Location = New-Object System.Drawing.Point(20, 172)
$runtimeCaptionLabel.Size = New-Object System.Drawing.Size(105, 24)
$runtimeCaptionLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$form.Controls.Add($runtimeCaptionLabel)

$runtimeStatusLabel = New-Object System.Windows.Forms.Label
$runtimeStatusLabel.Location = New-Object System.Drawing.Point(130, 172)
$runtimeStatusLabel.Size = New-Object System.Drawing.Size(180, 24)
$runtimeStatusLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($runtimeStatusLabel)

$sdkCaptionLabel = New-Object System.Windows.Forms.Label
$sdkCaptionLabel.Text = ".NET SDK (8+):"
$sdkCaptionLabel.Location = New-Object System.Drawing.Point(20, 198)
$sdkCaptionLabel.Size = New-Object System.Drawing.Size(105, 24)
$sdkCaptionLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$form.Controls.Add($sdkCaptionLabel)

$sdkStatusLabel = New-Object System.Windows.Forms.Label
$sdkStatusLabel.Location = New-Object System.Drawing.Point(130, 198)
$sdkStatusLabel.Size = New-Object System.Drawing.Size(260, 24)
$sdkStatusLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($sdkStatusLabel)

$prereqHintLabel = New-Object System.Windows.Forms.Label
$prereqHintLabel.Text = ".NET installers use winget. If winget is unavailable, install 'App Installer' from Microsoft Store."
$prereqHintLabel.Location = New-Object System.Drawing.Point(20, 225)
$prereqHintLabel.Size = New-Object System.Drawing.Size(730, 24)
$prereqHintLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$form.Controls.Add($prereqHintLabel)

$installButton = New-Object System.Windows.Forms.Button
$installButton.Text = "Install"
$installButton.Location = New-Object System.Drawing.Point(20, 262)
$installButton.Size = New-Object System.Drawing.Size(110, 34)
$form.Controls.Add($installButton)

$uninstallButton = New-Object System.Windows.Forms.Button
$uninstallButton.Text = "Uninstall"
$uninstallButton.Location = New-Object System.Drawing.Point(140, 262)
$uninstallButton.Size = New-Object System.Drawing.Size(110, 34)
$form.Controls.Add($uninstallButton)

$startNowButton = New-Object System.Windows.Forms.Button
$startNowButton.Text = "Start SleekPicker Now"
$startNowButton.Location = New-Object System.Drawing.Point(260, 262)
$startNowButton.Size = New-Object System.Drawing.Size(160, 34)
$form.Controls.Add($startNowButton)

$stopNowButton = New-Object System.Windows.Forms.Button
$stopNowButton.Text = "Stop SleekPicker"
$stopNowButton.Location = New-Object System.Drawing.Point(430, 262)
$stopNowButton.Size = New-Object System.Drawing.Size(150, 34)
$form.Controls.Add($stopNowButton)

$installRuntimeButton = New-Object System.Windows.Forms.Button
$installRuntimeButton.Text = "Install .NET 8 Runtime"
$installRuntimeButton.Location = New-Object System.Drawing.Point(20, 302)
$installRuntimeButton.Size = New-Object System.Drawing.Size(180, 34)
$form.Controls.Add($installRuntimeButton)

$installSdkButton = New-Object System.Windows.Forms.Button
$installSdkButton.Text = "Install .NET 8 SDK"
$installSdkButton.Location = New-Object System.Drawing.Point(210, 302)
$installSdkButton.Size = New-Object System.Drawing.Size(170, 34)
$form.Controls.Add($installSdkButton)

$closeButton = New-Object System.Windows.Forms.Button
$closeButton.Text = "Close"
$closeButton.Location = New-Object System.Drawing.Point(640, 302)
$closeButton.Size = New-Object System.Drawing.Size(110, 34)
$closeButton.Add_Click({ $form.Close() })
$form.Controls.Add($closeButton)

function Refresh-InstallerState
{
    $installedCommand = Get-InstalledAutorunCommand
    $currentReleaseExe = Resolve-AppExecutablePath
    $expectedCommand = if ($currentReleaseExe) { "`"$currentReleaseExe`"" } else { $null }
    $runtimeInstalled = Test-DotNet8RuntimeInstalled
    $sdkStatus = Get-DotNetSdkStatus
    $sdkInstalled = [bool]$sdkStatus.IsInstalled
    $runningProcessIds = @(Get-RunningSleekPickerProcessIds)
    $isRunning = $runningProcessIds.Count -gt 0

    if ($installedCommand)
    {
        $statusValueLabel.Text = "Installed"
        $statusValueLabel.ForeColor = [System.Drawing.Color]::FromArgb(0, 125, 0)
        if ($expectedCommand -and $installedCommand -eq $expectedCommand)
        {
            $statusDetailsLabel.Text = "Entry points to the current Release build."
        }
        else
        {
            $statusDetailsLabel.Text = "Entry exists but command differs from current Release build."
        }
    }
    else
    {
        $statusValueLabel.Text = "Not installed"
        $statusValueLabel.ForeColor = [System.Drawing.Color]::FromArgb(170, 30, 30)
        $statusDetailsLabel.Text = "No autorun entry configured for this user."
    }

    $commandValueBox.Text = if ($installedCommand) { $installedCommand } else { "(not set)" }
    $uninstallButton.Enabled = [bool]$installedCommand
    $runtimeStatusLabel.Text = if ($runtimeInstalled) { "Installed" } else { "Missing" }
    $runtimeStatusLabel.ForeColor = if ($runtimeInstalled) { [System.Drawing.Color]::FromArgb(0, 125, 0) } else { [System.Drawing.Color]::FromArgb(170, 30, 30) }
    $sdkStatusLabel.Text = [string]$sdkStatus.Display
    $sdkStatusLabel.ForeColor = if ($sdkInstalled) { [System.Drawing.Color]::FromArgb(0, 125, 0) } else { [System.Drawing.Color]::FromArgb(170, 30, 30) }
    $installRuntimeButton.Enabled = -not $runtimeInstalled
    $installSdkButton.Enabled = -not $sdkInstalled
    $stopNowButton.Enabled = $isRunning
}

$installButton.Add_Click(
    {
        $form.UseWaitCursor = $true
        $installButton.Enabled = $false
        $uninstallButton.Enabled = $false
        $startNowButton.Enabled = $false
        $stopNowButton.Enabled = $false
        $installRuntimeButton.Enabled = $false
        $installSdkButton.Enabled = $false
        [System.Windows.Forms.Application]::DoEvents()

        try
        {
            $exePath = Ensure-AppExecutable
            $command = "`"$exePath`""
            Set-AutorunCommand -command $command

            Refresh-InstallerState
            [System.Windows.Forms.MessageBox]::Show(
                $form,
                "Autorun entry installed for this user.`r`n`r`n$command",
                "SleekPicker Installer",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
        }
        catch
        {
            [System.Windows.Forms.MessageBox]::Show(
                $form,
                "Install failed:`r`n$($_.Exception.Message)",
                "SleekPicker Installer",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        }
        finally
        {
            $form.UseWaitCursor = $false
            $installButton.Enabled = $true
            $startNowButton.Enabled = $true
            $stopNowButton.Enabled = $true
            Refresh-InstallerState
        }
    })

$uninstallButton.Add_Click(
    {
        $form.UseWaitCursor = $true
        $installButton.Enabled = $false
        $uninstallButton.Enabled = $false
        $startNowButton.Enabled = $false
        $stopNowButton.Enabled = $false
        $installRuntimeButton.Enabled = $false
        $installSdkButton.Enabled = $false
        [System.Windows.Forms.Application]::DoEvents()

        try
        {
            Remove-AutorunCommand
            Refresh-InstallerState
            [System.Windows.Forms.MessageBox]::Show(
                $form,
                "Autorun entry removed.",
                "SleekPicker Installer",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
        }
        catch
        {
            [System.Windows.Forms.MessageBox]::Show(
                $form,
                "Uninstall failed:`r`n$($_.Exception.Message)",
                "SleekPicker Installer",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        }
        finally
        {
            $form.UseWaitCursor = $false
            $installButton.Enabled = $true
            $startNowButton.Enabled = $true
            $stopNowButton.Enabled = $true
            Refresh-InstallerState
        }
    })

$startNowButton.Add_Click(
    {
        $form.UseWaitCursor = $true
        $installButton.Enabled = $false
        $uninstallButton.Enabled = $false
        $startNowButton.Enabled = $false
        $stopNowButton.Enabled = $false
        $installRuntimeButton.Enabled = $false
        $installSdkButton.Enabled = $false
        [System.Windows.Forms.Application]::DoEvents()

        try
        {
            $result = Start-SleekPickerNow
            if ($result.AlreadyRunning)
            {
                [System.Windows.Forms.MessageBox]::Show(
                    $form,
                    "SleekPicker is already running.`r`nPID: $($result.ProcessId)",
                    "SleekPicker Installer",
                    [System.Windows.Forms.MessageBoxButtons]::OK,
                    [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
            }
            else
            {
                [System.Windows.Forms.MessageBox]::Show(
                    $form,
                    "SleekPicker started successfully.`r`nPID: $($result.ProcessId)",
                    "SleekPicker Installer",
                    [System.Windows.Forms.MessageBoxButtons]::OK,
                    [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
            }
        }
        catch
        {
            [System.Windows.Forms.MessageBox]::Show(
                $form,
                "Start failed:`r`n$($_.Exception.Message)",
                "SleekPicker Installer",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        }
        finally
        {
            $form.UseWaitCursor = $false
            $installButton.Enabled = $true
            $startNowButton.Enabled = $true
            $stopNowButton.Enabled = $true
            Refresh-InstallerState
        }
    })

$stopNowButton.Add_Click(
    {
        $form.UseWaitCursor = $true
        $installButton.Enabled = $false
        $uninstallButton.Enabled = $false
        $startNowButton.Enabled = $false
        $stopNowButton.Enabled = $false
        $installRuntimeButton.Enabled = $false
        $installSdkButton.Enabled = $false
        [System.Windows.Forms.Application]::DoEvents()

        try
        {
            $result = Stop-SleekPickerNow
            if ($result.StoppedCount -gt 0)
            {
                $pidList = ($result.StoppedIds -join ", ")
                [System.Windows.Forms.MessageBox]::Show(
                    $form,
                    "Stopped SleekPicker process(es).`r`nPID: $pidList",
                    "SleekPicker Installer",
                    [System.Windows.Forms.MessageBoxButtons]::OK,
                    [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
            }
            else
            {
                [System.Windows.Forms.MessageBox]::Show(
                    $form,
                    "SleekPicker is not running.",
                    "SleekPicker Installer",
                    [System.Windows.Forms.MessageBoxButtons]::OK,
                    [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
            }
        }
        catch
        {
            [System.Windows.Forms.MessageBox]::Show(
                $form,
                "Stop failed:`r`n$($_.Exception.Message)",
                "SleekPicker Installer",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        }
        finally
        {
            $form.UseWaitCursor = $false
            $installButton.Enabled = $true
            $startNowButton.Enabled = $true
            $stopNowButton.Enabled = $true
            Refresh-InstallerState
        }
    })

$installRuntimeButton.Add_Click(
    {
        $form.UseWaitCursor = $true
        $installButton.Enabled = $false
        $uninstallButton.Enabled = $false
        $startNowButton.Enabled = $false
        $stopNowButton.Enabled = $false
        $installRuntimeButton.Enabled = $false
        $installSdkButton.Enabled = $false
        [System.Windows.Forms.Application]::DoEvents()

        try
        {
            $result = Install-WingetPackage -id "Microsoft.DotNet.Runtime.8" -name ".NET 8 Runtime"
            $restartNotice = if ($result.ExitCode -eq 3010) { "`r`n`r`nA system restart is required to complete installation." } else { "" }

            [System.Windows.Forms.MessageBox]::Show(
                $form,
                ".NET 8 Runtime installation completed.$restartNotice",
                "SleekPicker Installer",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
        }
        catch
        {
            [System.Windows.Forms.MessageBox]::Show(
                $form,
                "Runtime install failed:`r`n$($_.Exception.Message)",
                "SleekPicker Installer",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        }
        finally
        {
            $form.UseWaitCursor = $false
            $installButton.Enabled = $true
            $startNowButton.Enabled = $true
            $stopNowButton.Enabled = $true
            Refresh-InstallerState
        }
    })

$installSdkButton.Add_Click(
    {
        $form.UseWaitCursor = $true
        $installButton.Enabled = $false
        $uninstallButton.Enabled = $false
        $startNowButton.Enabled = $false
        $stopNowButton.Enabled = $false
        $installRuntimeButton.Enabled = $false
        $installSdkButton.Enabled = $false
        [System.Windows.Forms.Application]::DoEvents()

        try
        {
            $result = Install-WingetPackage -id "Microsoft.DotNet.SDK.8" -name ".NET 8 SDK"
            $restartNotice = if ($result.ExitCode -eq 3010) { "`r`n`r`nA system restart is required to complete installation." } else { "" }

            [System.Windows.Forms.MessageBox]::Show(
                $form,
                ".NET 8 SDK installation completed.$restartNotice",
                "SleekPicker Installer",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
        }
        catch
        {
            [System.Windows.Forms.MessageBox]::Show(
                $form,
                "SDK install failed:`r`n$($_.Exception.Message)",
                "SleekPicker Installer",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        }
        finally
        {
            $form.UseWaitCursor = $false
            $installButton.Enabled = $true
            $startNowButton.Enabled = $true
            $stopNowButton.Enabled = $true
            Refresh-InstallerState
        }
    })

try
{
    Refresh-InstallerState
    [void]$form.ShowDialog()
}
catch
{
    [System.Windows.Forms.MessageBox]::Show(
        "Installer failed to start.`r`n$($_.Exception.Message)",
        "SleekPicker Installer",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
}
