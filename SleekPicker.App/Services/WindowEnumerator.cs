using System.Diagnostics;
using System.Linq;

namespace SleekPicker.App;

internal sealed class WindowEnumerator
{
    private readonly AppLogger _logger;
    private readonly VirtualDesktopService _desktopService;

    public WindowEnumerator(AppLogger logger, VirtualDesktopService desktopService)
    {
        _logger = logger;
        _desktopService = desktopService;
    }

    public IReadOnlyList<WindowEntry> EnumerateWindows(AppConfig config, IntPtr ownWindowHandle)
    {
        var desktopSnapshot = _desktopService.GetDesktopSnapshot();
        var windows = EnumerateInternal(config, ownWindowHandle, desktopSnapshot, strictFiltering: true);

        if (windows.Count == 0)
        {
            _logger.Warn("Primary window filter returned zero windows; retrying with fallback filter.");
            windows = EnumerateInternal(config, ownWindowHandle, desktopSnapshot, strictFiltering: false);
        }

        return windows
            .OrderBy(window => window.DesktopName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private List<WindowEntry> EnumerateInternal(
        AppConfig config,
        IntPtr ownWindowHandle,
        DesktopSnapshot desktopSnapshot,
        bool strictFiltering)
    {
        var windows = new List<WindowEntry>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            try
            {
                var include = strictFiltering
                    ? ShouldIncludeWindow(hWnd, ownWindowHandle, config)
                    : ShouldIncludeWindowFallback(hWnd, ownWindowHandle, config);

                if (!include)
                {
                    return true;
                }

                var title = NativeMethods.GetWindowTitle(hWnd).Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    if (config.ExcludeUntitledWindows)
                    {
                        return true;
                    }

                    title = "<Untitled Window>";
                }

                var processInfo = TryGetProcessInfo(hWnd);
                var desktopId = _desktopService.TryGetWindowDesktopId(hWnd);
                var desktopName = ResolveDesktopName(desktopId, desktopSnapshot, config);
                var icon = WindowIconProvider.GetIcon(hWnd, processInfo.Path);

                windows.Add(new WindowEntry
                {
                    Handle = hWnd,
                    Title = title,
                    ProcessName = processInfo.Name,
                    DesktopName = desktopName,
                    DesktopId = desktopId,
                    Icon = icon,
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"Skipping a window due to an error: {ex.Message}");
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static bool ShouldIncludeWindow(IntPtr hWnd, IntPtr ownWindowHandle, AppConfig config)
    {
        if (hWnd == IntPtr.Zero || hWnd == ownWindowHandle)
        {
            return false;
        }

        var visible = NativeMethods.IsWindowVisible(hWnd);
        var cloaked = NativeMethods.IsWindowCloaked(hWnd);
        if (!visible && !cloaked)
        {
            return false;
        }

        if (!config.IncludeMinimizedWindows && NativeMethods.IsIconic(hWnd))
        {
            return false;
        }

        if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
        {
            return false;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((extendedStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId)
        {
            return false;
        }

        return true;
    }

    private static bool ShouldIncludeWindowFallback(IntPtr hWnd, IntPtr ownWindowHandle, AppConfig config)
    {
        if (hWnd == IntPtr.Zero || hWnd == ownWindowHandle)
        {
            return false;
        }

        if (!NativeMethods.IsWindowVisible(hWnd))
        {
            return false;
        }

        if (!config.IncludeMinimizedWindows && NativeMethods.IsIconic(hWnd))
        {
            return false;
        }

        if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId)
        {
            return false;
        }

        return true;
    }

    private static (string Name, string? Path) TryGetProcessInfo(IntPtr hWnd)
    {
        _ = NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0)
        {
            return ("Unknown", null);
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName;
            string? path = null;

            try
            {
                path = process.MainModule?.FileName;
            }
            catch
            {
                path = null;
            }

            return (processName, path);
        }
        catch
        {
            return ("Unknown", null);
        }
    }

    private static string ResolveDesktopName(Guid? desktopId, DesktopSnapshot snapshot, AppConfig config)
    {
        if (!desktopId.HasValue)
        {
            return config.UnknownSectionName;
        }

        if (snapshot.NameById.TryGetValue(desktopId.Value, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return config.UnknownSectionName;
    }
}
