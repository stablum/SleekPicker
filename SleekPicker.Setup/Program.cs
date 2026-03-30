using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SleekPicker.Setup
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
        }
    }

    internal sealed class InstallerForm : Form
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunEntryName = "SleekPicker";

        private readonly string _repoRoot;
        private readonly string _version;

        private readonly Label _statusValueLabel;
        private readonly Label _statusDetailsLabel;
        private readonly TextBox _commandValueBox;
        private readonly Label _runtimeStatusLabel;
        private readonly Label _sdkStatusLabel;

        private readonly Button _installButton;
        private readonly Button _uninstallButton;
        private readonly Button _startNowButton;
        private readonly Button _stopNowButton;
        private readonly Button _installRuntimeButton;
        private readonly Button _installSdkButton;

        public InstallerForm()
        {
            _repoRoot = ResolveRepoRoot();
            _version = ReadVersionLabel();

            Text = "SleekPicker Installer";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(780, 390);
            MinimumSize = new Size(780, 390);
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;

            var titleLabel = new Label
            {
                Text = "SleekPicker Autorun Setup",
                Location = new Point(20, 18),
                Size = new Size(420, 28),
                Font = new Font("Segoe UI", 12, FontStyle.Bold)
            };
            Controls.Add(titleLabel);

            var versionCaptionLabel = new Label
            {
                Text = "Version:",
                Location = new Point(20, 58),
                Size = new Size(70, 24),
                Font = new Font("Segoe UI", 9)
            };
            Controls.Add(versionCaptionLabel);

            var versionValueLabel = new Label
            {
                Text = _version,
                Location = new Point(90, 58),
                Size = new Size(200, 24),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            Controls.Add(versionValueLabel);

            var statusCaptionLabel = new Label
            {
                Text = "Autorun:",
                Location = new Point(20, 85),
                Size = new Size(70, 24),
                Font = new Font("Segoe UI", 9)
            };
            Controls.Add(statusCaptionLabel);

            _statusValueLabel = new Label
            {
                Location = new Point(90, 85),
                Size = new Size(240, 24),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            Controls.Add(_statusValueLabel);

            _statusDetailsLabel = new Label
            {
                Location = new Point(20, 112),
                Size = new Size(730, 24),
                Font = new Font("Segoe UI", 9)
            };
            Controls.Add(_statusDetailsLabel);

            var commandCaptionLabel = new Label
            {
                Text = "Current entry:",
                Location = new Point(20, 142),
                Size = new Size(90, 24),
                Font = new Font("Segoe UI", 9)
            };
            Controls.Add(commandCaptionLabel);

            _commandValueBox = new TextBox
            {
                Location = new Point(112, 140),
                Size = new Size(638, 24),
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_commandValueBox);

            var runtimeCaptionLabel = new Label
            {
                Text = ".NET 8 Runtime:",
                Location = new Point(20, 172),
                Size = new Size(105, 24),
                Font = new Font("Segoe UI", 9)
            };
            Controls.Add(runtimeCaptionLabel);

            _runtimeStatusLabel = new Label
            {
                Location = new Point(130, 172),
                Size = new Size(180, 24),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            Controls.Add(_runtimeStatusLabel);

            var sdkCaptionLabel = new Label
            {
                Text = ".NET SDK (8+):",
                Location = new Point(20, 198),
                Size = new Size(105, 24),
                Font = new Font("Segoe UI", 9)
            };
            Controls.Add(sdkCaptionLabel);

            _sdkStatusLabel = new Label
            {
                Location = new Point(130, 198),
                Size = new Size(260, 24),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            Controls.Add(_sdkStatusLabel);

            var prereqHintLabel = new Label
            {
                Text = ".NET installers use winget. If winget is unavailable, install 'App Installer' from Microsoft Store.",
                Location = new Point(20, 225),
                Size = new Size(730, 24),
                Font = new Font("Segoe UI", 9)
            };
            Controls.Add(prereqHintLabel);

            _installButton = new Button
            {
                Text = "Install",
                Location = new Point(20, 262),
                Size = new Size(110, 34)
            };
            _installButton.Click += delegate { RunAction(InstallAutorun); };
            Controls.Add(_installButton);

            _uninstallButton = new Button
            {
                Text = "Uninstall",
                Location = new Point(140, 262),
                Size = new Size(110, 34)
            };
            _uninstallButton.Click += delegate { RunAction(UninstallAutorun); };
            Controls.Add(_uninstallButton);

            _startNowButton = new Button
            {
                Text = "Start SleekPicker Now",
                Location = new Point(260, 262),
                Size = new Size(160, 34)
            };
            _startNowButton.Click += delegate { RunAction(StartSleekPickerNow); };
            Controls.Add(_startNowButton);

            _stopNowButton = new Button
            {
                Text = "Stop SleekPicker",
                Location = new Point(430, 262),
                Size = new Size(150, 34)
            };
            _stopNowButton.Click += delegate { RunAction(StopSleekPickerNow); };
            Controls.Add(_stopNowButton);

            _installRuntimeButton = new Button
            {
                Text = "Install .NET 8 Runtime",
                Location = new Point(20, 302),
                Size = new Size(180, 34)
            };
            _installRuntimeButton.Click += delegate { RunAction(delegate { InstallWingetPackage("Microsoft.DotNet.Runtime.8", ".NET 8 Runtime"); }); };
            Controls.Add(_installRuntimeButton);

            _installSdkButton = new Button
            {
                Text = "Install .NET 8 SDK",
                Location = new Point(210, 302),
                Size = new Size(170, 34)
            };
            _installSdkButton.Click += delegate { RunAction(delegate { InstallWingetPackage("Microsoft.DotNet.SDK.8", ".NET 8 SDK"); }); };
            Controls.Add(_installSdkButton);

            var closeButton = new Button
            {
                Text = "Close",
                Location = new Point(640, 302),
                Size = new Size(110, 34)
            };
            closeButton.Click += delegate { Close(); };
            Controls.Add(closeButton);

            RefreshInstallerState();
        }

        private void RunAction(Action action)
        {
            UseWaitCursor = true;
            SetPrimaryButtonsEnabled(false);
            Application.DoEvents();

            try
            {
                action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "SleekPicker Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
                SetPrimaryButtonsEnabled(true);
                RefreshInstallerState();
            }
        }

        private void SetPrimaryButtonsEnabled(bool enabled)
        {
            _installButton.Enabled = enabled;
            _uninstallButton.Enabled = enabled;
            _startNowButton.Enabled = enabled;
            _stopNowButton.Enabled = enabled;
            _installRuntimeButton.Enabled = enabled;
            _installSdkButton.Enabled = enabled;
        }

        private void RefreshInstallerState()
        {
            var installedCommand = GetInstalledAutorunCommand();
            var currentReleaseExe = ResolveAppExecutablePath();
            var expectedCommand = string.IsNullOrWhiteSpace(currentReleaseExe) ? null : Quote(currentReleaseExe);

            var runtimeInstalled = TestDotNet8RuntimeInstalled();
            var sdkStatus = GetDotNetSdkStatus();
            var runningProcessIds = GetRunningSleekPickerProcessIds();

            if (!string.IsNullOrWhiteSpace(installedCommand))
            {
                _statusValueLabel.Text = "Installed";
                _statusValueLabel.ForeColor = Color.FromArgb(0, 125, 0);
                _statusDetailsLabel.Text = expectedCommand != null && string.Equals(installedCommand, expectedCommand, StringComparison.OrdinalIgnoreCase)
                    ? "Entry points to the current Release build."
                    : "Entry exists but command differs from current Release build.";
            }
            else
            {
                _statusValueLabel.Text = "Not installed";
                _statusValueLabel.ForeColor = Color.FromArgb(170, 30, 30);
                _statusDetailsLabel.Text = "No autorun entry configured for this user.";
            }

            _commandValueBox.Text = string.IsNullOrWhiteSpace(installedCommand) ? "(not set)" : installedCommand;
            _uninstallButton.Enabled = !string.IsNullOrWhiteSpace(installedCommand);

            _runtimeStatusLabel.Text = runtimeInstalled ? "Installed" : "Missing";
            _runtimeStatusLabel.ForeColor = runtimeInstalled ? Color.FromArgb(0, 125, 0) : Color.FromArgb(170, 30, 30);

            _sdkStatusLabel.Text = sdkStatus.Display;
            _sdkStatusLabel.ForeColor = sdkStatus.IsInstalled ? Color.FromArgb(0, 125, 0) : Color.FromArgb(170, 30, 30);

            _installRuntimeButton.Enabled = !runtimeInstalled;
            _installSdkButton.Enabled = !sdkStatus.IsInstalled;
            _stopNowButton.Enabled = runningProcessIds.Count > 0;
        }

        private void InstallAutorun()
        {
            var exePath = EnsureAppExecutable();
            var command = Quote(exePath);

            using (var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, true))
            {
                if (runKey == null)
                {
                    throw new InvalidOperationException("Unable to open autorun registry key.");
                }

                runKey.SetValue(RunEntryName, command, RegistryValueKind.String);
            }

            MessageBox.Show(
                this,
                string.Format("Autorun entry installed for this user.\r\n\r\n{0}", command),
                "SleekPicker Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void UninstallAutorun()
        {
            using (var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, true))
            {
                if (runKey != null)
                {
                    runKey.DeleteValue(RunEntryName, false);
                }
            }

            MessageBox.Show(
                this,
                "Autorun entry removed.",
                "SleekPicker Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void StartSleekPickerNow()
        {
            var exePath = EnsureAppExecutable();
            var running = TryGetRunningProcessForPath(exePath);
            if (running.HasValue)
            {
                MessageBox.Show(
                    this,
                    string.Format("SleekPicker is already running.\r\nPID: {0}", running.Value),
                    "SleekPicker Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? _repoRoot,
                UseShellExecute = true
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("SleekPicker did not start.");
            }

            MessageBox.Show(
                this,
                string.Format("SleekPicker started successfully.\r\nPID: {0}", process.Id),
                "SleekPicker Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void StopSleekPickerNow()
        {
            var runningIds = GetRunningSleekPickerProcessIds();
            var stoppedIds = new List<int>();

            foreach (var processId in runningIds)
            {
                try
                {
                    using (var process = Process.GetProcessById(processId))
                    {
                        process.Kill();
                    }

                    stoppedIds.Add(processId);
                }
                catch
                {
                    // Ignore race conditions where process exits while we stop it.
                }
            }

            if (stoppedIds.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Format("Stopped SleekPicker process(es).\r\nPID: {0}", string.Join(", ", stoppedIds)),
                    "SleekPicker Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(
                this,
                "SleekPicker is not running.",
                "SleekPicker Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void InstallWingetPackage(string packageId, string displayName)
        {
            var wingetCheck = RunProcess("winget", "--version", _repoRoot, true);
            if (wingetCheck.ExitCode != 0)
            {
                throw new InvalidOperationException("winget is not available on this machine. Install App Installer from Microsoft Store first.");
            }

            var args = string.Join(" ", new[]
            {
                "install",
                "--id", packageId,
                "--exact",
                "--source", "winget",
                "--accept-source-agreements",
                "--accept-package-agreements",
                "--silent",
                "--disable-interactivity"
            });

            var result = RunProcess("winget", args, _repoRoot, true);
            if (result.ExitCode != 0 && result.ExitCode != 3010)
            {
                var details = string.IsNullOrWhiteSpace(result.Output)
                    ? "No additional output."
                    : result.Output.Trim();
                throw new InvalidOperationException(string.Format("{0} installation failed (exit code {1}).\r\n{2}", displayName, result.ExitCode, details));
            }

            var restartNotice = result.ExitCode == 3010
                ? "\r\n\r\nA system restart is required to complete installation."
                : string.Empty;

            MessageBox.Show(
                this,
                string.Format("{0} installation completed.{1}", displayName, restartNotice),
                "SleekPicker Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private string EnsureAppExecutable()
        {
            var existing = ResolveAppExecutablePath();
            if (!string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
            {
                return existing;
            }

            var solutionPath = Path.Combine(_repoRoot, "SleekPicker.slnx");
            if (!File.Exists(solutionPath))
            {
                throw new InvalidOperationException(string.Format("Cannot locate solution file: {0}", solutionPath));
            }

            var buildResult = RunProcess("dotnet", string.Format("build \"{0}\" -c Release", solutionPath), _repoRoot, true);
            if (buildResult.ExitCode != 0)
            {
                var details = string.IsNullOrWhiteSpace(buildResult.Output)
                    ? "No additional output."
                    : buildResult.Output.Trim();
                throw new InvalidOperationException(string.Format("Build failed. Autorun was not installed.\r\n{0}", details));
            }

            var built = ResolveAppExecutablePath();
            if (string.IsNullOrWhiteSpace(built))
            {
                throw new InvalidOperationException("Build succeeded but SleekPicker.App.exe was not found in Release output.");
            }

            return built;
        }

        private string ResolveAppExecutablePath()
        {
            var binPath = Path.Combine(_repoRoot, "SleekPicker.App", "bin");
            if (!Directory.Exists(binPath))
            {
                return null;
            }

            var releaseMarker = string.Format("{0}Release{0}", Path.DirectorySeparatorChar);
            var executables = Directory
                .EnumerateFiles(binPath, "SleekPicker.App.exe", SearchOption.AllDirectories)
                .Where(path => path.IndexOf(releaseMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            return executables.Count == 0 ? null : executables[0].FullName;
        }

        private int? TryGetRunningProcessForPath(string exePath)
        {
            var normalizedTarget = Path.GetFullPath(exePath);

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, ExecutablePath FROM Win32_Process WHERE Name='SleekPicker.App.exe'"))
                {
                    foreach (ManagementObject process in searcher.Get())
                    {
                        var executablePath = process["ExecutablePath"] as string;
                        if (string.IsNullOrWhiteSpace(executablePath))
                        {
                            continue;
                        }

                        if (!string.Equals(Path.GetFullPath(executablePath), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        return Convert.ToInt32(process["ProcessId"]);
                    }
                }
            }
            catch
            {
                // Best effort only.
            }

            return null;
        }

        private List<int> GetRunningSleekPickerProcessIds()
        {
            var processIds = new HashSet<int>();

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.ProcessName.StartsWith("SleekPicker", StringComparison.OrdinalIgnoreCase))
                    {
                        processIds.Add(process.Id);
                    }
                }
                catch
                {
                    // Ignore inaccessible process entries.
                }
                finally
                {
                    process.Dispose();
                }
            }

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine FROM Win32_Process WHERE Name='dotnet.exe'"))
                {
                    foreach (ManagementObject process in searcher.Get())
                    {
                        var commandLine = process["CommandLine"] as string;
                        if (string.IsNullOrWhiteSpace(commandLine))
                        {
                            continue;
                        }

                        if (commandLine.IndexOf("SleekPicker.App.dll", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            processIds.Add(Convert.ToInt32(process["ProcessId"]));
                        }
                    }
                }
            }
            catch
            {
                // Best effort only.
            }

            return processIds.OrderBy(id => id).ToList();
        }

        private string GetInstalledAutorunCommand()
        {
            try
            {
                using (var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return runKey != null ? runKey.GetValue(RunEntryName) as string : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private bool TestDotNet8RuntimeInstalled()
        {
            try
            {
                var result = RunProcess("dotnet", "--list-runtimes", _repoRoot, true);
                if (result.ExitCode != 0)
                {
                    return false;
                }

                return result.Output
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(line => line.StartsWith("Microsoft.NETCore.App 8.", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private SdkStatus GetDotNetSdkStatus()
        {
            try
            {
                var result = RunProcess("dotnet", "--list-sdks", _repoRoot, true);
                if (result.ExitCode != 0)
                {
                    return new SdkStatus(false, "Missing");
                }

                var parsedVersions = new List<Version>();
                var lines = result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var token = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        continue;
                    }

                    var normalized = token.Split('-')[0];
                    Version version;
                    if (Version.TryParse(normalized, out version))
                    {
                        parsedVersions.Add(version);
                    }
                }

                if (parsedVersions.Count == 0)
                {
                    return new SdkStatus(false, "Missing");
                }

                var highest = parsedVersions.OrderByDescending(v => v).First();
                if (highest.Major >= 8)
                {
                    return new SdkStatus(true, string.Format("Installed ({0})", highest));
                }

                return new SdkStatus(false, string.Format("Missing (found {0})", highest));
            }
            catch
            {
                return new SdkStatus(false, "Missing");
            }
        }

        private static ProcessResult RunProcess(string fileName, string arguments, string workingDirectory, bool hideWindow)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = hideWindow,
                WindowStyle = hideWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException(string.Format("Failed to start process: {0}", fileName));
                }

                var standardOutput = process.StandardOutput.ReadToEnd();
                var standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();

                var outputParts = new[] { standardOutput, standardError }
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                var output = string.Join(Environment.NewLine, outputParts).Trim();

                return new ProcessResult(process.ExitCode, output);
            }
        }

        private static string ResolveRepoRoot()
        {
            var baseDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }

            var current = new DirectoryInfo(baseDirectory);
            while (current != null)
            {
                var versionPath = Path.Combine(current.FullName, "version.txt");
                var appProjectDir = Path.Combine(current.FullName, "SleekPicker.App");
                if (File.Exists(versionPath) && Directory.Exists(appProjectDir))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private string ReadVersionLabel()
        {
            var versionPath = Path.Combine(_repoRoot, "version.txt");
            if (!File.Exists(versionPath))
            {
                return "unknown";
            }

            var value = File.ReadAllText(versionPath).Trim();
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
        }

        private static string Quote(string value)
        {
            return string.Format("\"{0}\"", value);
        }

        private struct ProcessResult
        {
            public ProcessResult(int exitCode, string output)
            {
                ExitCode = exitCode;
                Output = output;
            }

            public int ExitCode { get; private set; }
            public string Output { get; private set; }
        }

        private struct SdkStatus
        {
            public SdkStatus(bool isInstalled, string display)
            {
                IsInstalled = isInstalled;
                Display = display;
            }

            public bool IsInstalled { get; private set; }
            public string Display { get; private set; }
        }
    }
}
