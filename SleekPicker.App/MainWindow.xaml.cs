using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;
using MediaColor = System.Windows.Media.Color;
using InputKey = System.Windows.Input.Key;

namespace SleekPicker.App;

public partial class MainWindow : Window
{
    private static readonly (string Label, InputKey PrimaryKey, InputKey? AlternateKey)[] ActivationShortcutBindings =
    {
        ("1", InputKey.D1, InputKey.NumPad1),
        ("2", InputKey.D2, InputKey.NumPad2),
        ("3", InputKey.D3, InputKey.NumPad3),
        ("4", InputKey.D4, InputKey.NumPad4),
        ("5", InputKey.D5, InputKey.NumPad5),
        ("6", InputKey.D6, InputKey.NumPad6),
        ("7", InputKey.D7, InputKey.NumPad7),
        ("8", InputKey.D8, InputKey.NumPad8),
        ("9", InputKey.D9, InputKey.NumPad9),
        ("0", InputKey.D0, InputKey.NumPad0),
        ("A", InputKey.A, null),
        ("B", InputKey.B, null),
        ("C", InputKey.C, null),
        ("D", InputKey.D, null),
        ("E", InputKey.E, null),
        ("F", InputKey.F, null),
        ("G", InputKey.G, null),
        ("H", InputKey.H, null),
        ("I", InputKey.I, null),
        ("J", InputKey.J, null),
        ("K", InputKey.K, null),
        ("L", InputKey.L, null),
        ("M", InputKey.M, null),
        ("N", InputKey.N, null),
        ("O", InputKey.O, null),
        ("P", InputKey.P, null),
        ("Q", InputKey.Q, null),
        ("R", InputKey.R, null),
        ("S", InputKey.S, null),
        ("T", InputKey.T, null),
        ("U", InputKey.U, null),
        ("V", InputKey.V, null),
        ("W", InputKey.W, null),
        ("X", InputKey.X, null),
        ("Y", InputKey.Y, null),
        ("Z", InputKey.Z, null),
    };

    private readonly AppConfig _config;
    private readonly AppLogger _logger;
    private readonly VirtualDesktopService _desktopService;
    private readonly WindowEnumerator _windowEnumerator;
    private readonly ObservableCollection<DesktopSection> _desktopSections = new();
    private readonly Dictionary<InputKey, WindowEntry> _windowByShortcutKey = new();
    private readonly DispatcherTimer _edgeTimer;
    private readonly DispatcherTimer _refreshTimer;
    private static readonly TimeSpan CursorOutsideHideDelay = TimeSpan.FromMilliseconds(140);

    private WorkArea _activeWorkArea;
    private DateTime _lastEdgeActivationUtc = DateTime.MinValue;
    private DateTime? _edgeHoverStartUtc;
    private DateTime? _cursorOutsidePanelStartUtc;
    private bool _edgeTriggerLockedUntilExit;
    private bool _isPanelVisible;
    private bool _isAnimating;

    internal MainWindow(AppConfig config, AppLogger logger)
    {
        _config = config;
        _logger = logger;
        _desktopService = new VirtualDesktopService(logger);
        _windowEnumerator = new WindowEnumerator(logger, _desktopService);

        InitializeComponent();

        DesktopSectionsControl.ItemsSource = _desktopSections;

        ApplyUiConfiguration();

        _edgeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(_config.EdgePollMs),
        };
        _edgeTimer.Tick += OnEdgeTimerTick;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(_config.RefreshIntervalMs),
        };
        _refreshTimer.Tick += (_, _) =>
        {
            if (_isPanelVisible)
            {
                RefreshWindowList();
            }
        };
    }

    public void HidePanelImmediate()
    {
        BeginAnimation(LeftProperty, null);
        _isAnimating = false;
        _isPanelVisible = false;
        _edgeHoverStartUtc = null;
        _cursorOutsidePanelStartUtc = null;

        _activeWorkArea = GetCurrentWorkArea();
        var workAreaDip = ToDipWorkArea(_activeWorkArea);
        Width = Math.Min(Math.Max(ToDipX(280), ToDipX(_config.PanelWidth)), workAreaDip.Width);
        Height = workAreaDip.Height;
        Top = workAreaDip.Top;
        Left = workAreaDip.Right + ToDipX(2);

        if (IsVisible)
        {
            Hide();
        }
    }

    public void ShowPanelNearCursor()
    {
        var workArea = GetCurrentWorkArea();
        ShowPanel(workArea);
    }

    public void RefreshWindowList()
    {
        try
        {
            var ownHandle = new WindowInteropHelper(this).Handle;
            var windows = _windowEnumerator.EnumerateWindows(_config, ownHandle);
            var desktopSnapshot = _desktopService.GetDesktopSnapshot();
            var desktopOrder = desktopSnapshot.Desktops.ToDictionary(desktop => desktop.Id, desktop => desktop.Index);

            var grouped = windows
                .GroupBy(window => window.DesktopId)
                .Select(group =>
                {
                    var sectionName = group.Key.HasValue && desktopSnapshot.NameById.TryGetValue(group.Key.Value, out var orderedName)
                        ? orderedName
                        : group.First().DesktopName;

                    var sortOrder = group.Key.HasValue && desktopOrder.TryGetValue(group.Key.Value, out var index)
                        ? index
                        : int.MaxValue;

                    return new
                    {
                        Name = sectionName,
                        SortOrder = sortOrder,
                        Windows = group.OrderBy(window => window.Title, StringComparer.OrdinalIgnoreCase).ToArray(),
                    };
                })
                .OrderBy(section => section.SortOrder)
                .ThenBy(section => section.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AssignActivationShortcuts(grouped.SelectMany(section => section.Windows));

            _desktopSections.Clear();
            foreach (var group in grouped)
            {
                _desktopSections.Add(new DesktopSection(group.Name, group.Windows));
            }

            EmptyStateText.Visibility = _desktopSections.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            LastUpdatedText.Text = $"Updated {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _windowByShortcutKey.Clear();
            _logger.Error("Failed to refresh window list.", ex);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _edgeTimer.Stop();
        _refreshTimer.Stop();
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BlurHelper.TryEnableBlur(this, _logger);
        VersionText.Text = $"v{VersionProvider.GetVersionLabel()}";
        LastUpdatedText.Text = "Waiting for edge activation";
        HidePanelImmediate();
        _edgeTimer.Start();
        _refreshTimer.Start();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_config.AutoHideOnFocusLoss && _isPanelVisible)
        {
            HidePanel();
        }
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isPanelVisible || _isAnimating)
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.Escape)
        {
            HidePanel();
            e.Handled = true;
            return;
        }

        if (TryActivateWindowByShortcut(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnMouseLeavePanel(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isPanelVisible && !_isAnimating)
        {
            HidePanel();
        }
    }

    private void OnPanelPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isPanelVisible || _isAnimating || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (HasAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) ||
            HasAncestor<System.Windows.Controls.Primitives.ScrollBar>(source))
        {
            return;
        }

        HidePanel();
        e.Handled = true;
    }

    private void OnWindowButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: WindowEntry entry })
        {
            return;
        }

        HidePanel();
        _ = ActivateWindowAsync(entry);
    }

    private bool TryActivateWindowByShortcut(InputKey key)
    {
        if (!_windowByShortcutKey.TryGetValue(key, out var entry))
        {
            return false;
        }

        HidePanel();
        _ = ActivateWindowAsync(entry);
        return true;
    }

    private async void OnCloseWindowButtonClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not System.Windows.Controls.Button { Tag: WindowEntry entry })
        {
            return;
        }

        try
        {
            var posted = NativeMethods.PostMessage(entry.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (!posted)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.Warn($"Failed to send close signal to '{entry.Title}' (Win32 error {error}).");
            }
            else
            {
                _logger.Info($"Sent close signal to '{entry.Title}'.");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to close window '{entry.Title}': {ex.Message}");
        }

        await Task.Delay(160);
        RefreshWindowList();
    }

    private async Task ActivateWindowAsync(WindowEntry entry)
    {
        await Task.Delay(120);

        if (entry.DesktopId.HasValue)
        {
            try
            {
                _ = await _desktopService.SwitchToDesktopAsync(entry.DesktopId.Value);
                await Task.Delay(80);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to switch to desktop for '{entry.Title}': {ex.Message}");
            }
        }

        try
        {
            // Only restore when minimized; restoring an already-maximized window
            // can force it back to its previous non-maximized bounds.
            if (NativeMethods.IsIconic(entry.Handle))
            {
                _ = NativeMethods.ShowWindowAsync(entry.Handle, NativeMethods.SW_RESTORE);
            }

            _ = NativeMethods.BringWindowToTop(entry.Handle);
            _ = NativeMethods.SetForegroundWindow(entry.Handle);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to activate window '{entry.Title}': {ex.Message}");
        }
    }

    private void OnEdgeTimerTick(object? sender, EventArgs e)
    {
        if (_isAnimating)
        {
            _edgeHoverStartUtc = null;
            _cursorOutsidePanelStartUtc = null;
            return;
        }

        if (_isPanelVisible)
        {
            MonitorPanelPointerState();
            return;
        }

        if (!NativeMethods.TryGetCursorPosition(out var cursor))
        {
            _edgeHoverStartUtc = null;
            return;
        }

        var workArea = NativeMethods.GetWorkAreaForPoint(cursor);
        if (cursor.Y < workArea.Top || cursor.Y > workArea.Bottom)
        {
            _edgeHoverStartUtc = null;
            _edgeTriggerLockedUntilExit = false;
            return;
        }

        var threshold = workArea.Right - _config.EdgeTriggerPixels;
        if (cursor.X < threshold)
        {
            _edgeHoverStartUtc = null;
            _edgeTriggerLockedUntilExit = false;
            return;
        }

        if (_edgeTriggerLockedUntilExit)
        {
            _edgeHoverStartUtc = null;
            return;
        }

        var now = DateTime.UtcNow;
        _edgeHoverStartUtc ??= now;

        var edgeHoldElapsedMs = (now - _edgeHoverStartUtc.Value).TotalMilliseconds;
        if (edgeHoldElapsedMs < _config.EdgeHoldMs)
        {
            return;
        }

        var elapsed = now - _lastEdgeActivationUtc;
        if (elapsed.TotalMilliseconds < _config.ActivationCooldownMs)
        {
            return;
        }

        _edgeTriggerLockedUntilExit = true;
        _edgeHoverStartUtc = null;
        ShowPanel(workArea);
    }

    private void AssignActivationShortcuts(IEnumerable<WindowEntry> windows)
    {
        _windowByShortcutKey.Clear();

        var index = 0;
        foreach (var window in windows)
        {
            if ((uint)index < (uint)ActivationShortcutBindings.Length)
            {
                var binding = ActivationShortcutBindings[index];
                window.ShortcutLabel = binding.Label;
                _windowByShortcutKey[binding.PrimaryKey] = window;
                if (binding.AlternateKey.HasValue)
                {
                    _windowByShortcutKey[binding.AlternateKey.Value] = window;
                }
            }
            else
            {
                window.ShortcutLabel = null;
            }

            index++;
        }
    }

    private void ShowPanel(WorkArea workArea)
    {
        _activeWorkArea = workArea;
        _lastEdgeActivationUtc = DateTime.UtcNow;
        _cursorOutsidePanelStartUtc = null;

        var workAreaDip = ToDipWorkArea(workArea);
        Width = Math.Min(Math.Max(ToDipX(280), ToDipX(_config.PanelWidth)), workAreaDip.Width);
        Height = workAreaDip.Height;
        Top = workAreaDip.Top;
        Left = workAreaDip.Right + ToDipX(2);

        if (!IsVisible)
        {
            Show();
        }

        RefreshWindowList();

        _isAnimating = true;
        var targetLeft = workAreaDip.Right - Width;

        var animation = new DoubleAnimation
        {
            To = targetLeft,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        animation.Completed += (_, _) =>
        {
            _isAnimating = false;
            _isPanelVisible = true;
            BeginAnimation(LeftProperty, null);
            Left = targetLeft;
            Activate();
            Focus();
        };

        BeginAnimation(LeftProperty, animation);
    }

    private void HidePanel()
    {
        if (!IsVisible || _isAnimating)
        {
            return;
        }

        _isAnimating = true;
        _cursorOutsidePanelStartUtc = null;

        var workAreaDip = ToDipWorkArea(_activeWorkArea);
        var hiddenLeft = workAreaDip.Right + ToDipX(2);
        var animation = new DoubleAnimation
        {
            To = hiddenLeft,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };

        animation.Completed += (_, _) =>
        {
            BeginAnimation(LeftProperty, null);
            Left = hiddenLeft;
            _isPanelVisible = false;
            _isAnimating = false;
            Hide();
        };

        BeginAnimation(LeftProperty, animation);
    }

    private void MonitorPanelPointerState()
    {
        if (!NativeMethods.TryGetCursorPosition(out var cursor))
        {
            _cursorOutsidePanelStartUtc = null;
            return;
        }

        if (IsCursorInsidePanel(cursor))
        {
            _cursorOutsidePanelStartUtc = null;
            return;
        }

        var now = DateTime.UtcNow;
        _cursorOutsidePanelStartUtc ??= now;
        if (now - _cursorOutsidePanelStartUtc.Value < CursorOutsideHideDelay)
        {
            return;
        }

        HidePanel();
    }

    private bool IsCursorInsidePanel(NativeMethods.POINT cursor)
    {
        var pointDip = ToDipPoint(cursor.X, cursor.Y);
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var right = Left + width;
        var bottom = Top + height;

        return pointDip.X >= Left &&
               pointDip.X <= right &&
               pointDip.Y >= Top &&
               pointDip.Y <= bottom;
    }

    private void ApplyUiConfiguration()
    {
        FontFamily = new System.Windows.Media.FontFamily(_config.FontFamily);
        FontSize = _config.FontSize;
        PanelSurface.CornerRadius = new CornerRadius(Math.Max(0, _config.CornerRadius));
        PanelSurface.Opacity = Math.Clamp(_config.SurfaceOpacity, 0.1, 1.0);

        SetBrushResource("PanelBackgroundBrush", _config.BackgroundColor, MediaColor.FromArgb(0xCC, 0x1A, 0x20, 0x2B));
        SetBrushResource("PanelBorderBrush", _config.BorderColor, MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        SetBrushResource("TextBrush", _config.TextColor, MediaColor.FromArgb(0xFF, 0xF5, 0xF8, 0xFF));
        SetBrushResource("SectionHeaderBrush", _config.SectionHeaderColor, MediaColor.FromArgb(0xFF, 0x9C, 0xD4, 0xFF));
        SetBrushResource("AccentBrush", _config.AccentColor, MediaColor.FromArgb(0xFF, 0x5F, 0xC9, 0xFF));
        SetBrushResource("ItemHoverBrush", _config.HoverColor, MediaColor.FromArgb(0x2F, 0xFF, 0xFF, 0xFF));
        SetBrushResource("ItemPressedBrush", _config.PressedColor, MediaColor.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
    }

    private void SetBrushResource(string key, string configuredColor, MediaColor fallback)
    {
        var color = TryParseColor(configuredColor, out var parsedColor)
            ? parsedColor
            : fallback;

        Resources[key] = new SolidColorBrush(color);
    }

    private static bool TryParseColor(string value, out MediaColor color)
    {
        try
        {
            var parsed = System.Windows.Media.ColorConverter.ConvertFromString(value);
            if (parsed is MediaColor converted)
            {
                color = converted;
                return true;
            }
        }
        catch
        {
            // Ignore parsing failures and use fallback.
        }

        color = default;
        return false;
    }

    private static WorkArea GetCurrentWorkArea()
    {
        if (NativeMethods.TryGetCursorPosition(out var cursor))
        {
            return NativeMethods.GetWorkAreaForPoint(cursor);
        }

        var fallback = SystemParameters.WorkArea;
        return new WorkArea((int)fallback.Left, (int)fallback.Top, (int)fallback.Right, (int)fallback.Bottom);
    }

    private Rect ToDipWorkArea(WorkArea workArea)
    {
        var topLeft = ToDipPoint(workArea.Left, workArea.Top);
        var bottomRight = ToDipPoint(workArea.Right, workArea.Bottom);
        return new Rect(topLeft, bottomRight);
    }

    private System.Windows.Point ToDipPoint(int xPixels, int yPixels)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            var matrix = source.CompositionTarget.TransformFromDevice;
            return matrix.Transform(new System.Windows.Point(xPixels, yPixels));
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var xScale = Math.Max(1, dpi.DpiScaleX);
        var yScale = Math.Max(1, dpi.DpiScaleY);
        return new System.Windows.Point(xPixels / xScale, yPixels / yScale);
    }

    private double ToDipX(double xPixels)
    {
        var origin = ToDipPoint(0, 0);
        var point = ToDipPoint((int)Math.Round(xPixels), 0);
        return Math.Max(1, point.X - origin.X);
    }

    private static bool HasAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = GetParent(current))
        {
            if (current is T)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(current);
        }

        if (current is FrameworkContentElement fce)
        {
            return fce.Parent;
        }

        return null;
    }
}
