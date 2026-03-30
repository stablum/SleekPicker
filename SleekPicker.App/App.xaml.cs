using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;
using System.Windows;
using System.Windows.Threading;

namespace SleekPicker.App;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private Forms.NotifyIcon? _trayIcon;
    private AppLogger? _logger;

    private void OnStartup(object sender, System.Windows.StartupEventArgs e)
    {
        var repoRoot = RuntimePaths.ResolveRepositoryRoot();
        var logPath = Path.Combine(repoRoot, "SleekPicker.log");
        _logger = new AppLogger(logPath);

        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var configPath = Path.Combine(repoRoot, "config.toml");
        var config = AppConfig.LoadOrCreate(configPath, _logger);

        _mainWindow = new MainWindow(config, _logger);
        MainWindow = _mainWindow;
        _mainWindow.Show();
        _mainWindow.HidePanelImmediate();

        ConfigureTrayIcon();
        _logger.Info($"Started SleekPicker v{VersionProvider.GetVersionLabel()}.");
    }

    private void ConfigureTrayIcon()
    {
        if (_mainWindow is null)
        {
            return;
        }

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("Open Panel", null, (_, _) => _mainWindow.ShowPanelNearCursor());
        contextMenu.Items.Add("Refresh", null, (_, _) => _mainWindow.RefreshWindowList());
        contextMenu.Items.Add("Exit", null, (_, _) => Shutdown());

        _trayIcon = new Forms.NotifyIcon
        {
            Text = $"SleekPicker {VersionProvider.GetVersionLabel()}",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = contextMenu,
        };

        _trayIcon.DoubleClick += (_, _) => _mainWindow.ShowPanelNearCursor();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled UI exception.", e.Exception);
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger?.Error("Unhandled domain exception.", exception);
            return;
        }

        _logger?.Error("Unhandled domain exception: unknown exception payload.");
    }

    private void OnExit(object sender, System.Windows.ExitEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        DispatcherUnhandledException -= OnDispatcherUnhandledException;

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _logger?.Info("SleekPicker stopped.");
    }
}
