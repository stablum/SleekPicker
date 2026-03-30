using System.IO;
using Tomlyn;
using Tomlyn.Model;

namespace SleekPicker.App;

internal sealed class AppConfig
{
    public int PanelWidth { get; set; } = 430;

    public double CornerRadius { get; set; } = 20;

    public string FontFamily { get; set; } = "Carbon Plus";

    public double FontSize { get; set; } = 14;

    public string BackgroundColor { get; set; } = "#CC1A202B";

    public string BorderColor { get; set; } = "#66FFFFFF";

    public string TextColor { get; set; } = "#FFF5F8FF";

    public string SectionHeaderColor { get; set; } = "#FF9CD4FF";

    public string AccentColor { get; set; } = "#FF5FC9FF";

    public string HoverColor { get; set; } = "#2FFFFFFF";

    public string PressedColor { get; set; } = "#44FFFFFF";

    public double SurfaceOpacity { get; set; } = 0.9;

    public int EdgePollMs { get; set; } = 80;

    public int RefreshIntervalMs { get; set; } = 3000;

    public int ActivationCooldownMs { get; set; } = 900;

    public int EdgeTriggerPixels { get; set; } = 1;

    public int EdgeHoldMs { get; set; } = 180;

    public bool AutoHideOnFocusLoss { get; set; } = true;

    public bool ExcludeUntitledWindows { get; set; } = true;

    public bool IncludeMinimizedWindows { get; set; } = true;

    public string UnknownSectionName { get; set; } = "Unknown Desktop";

    public static AppConfig LoadOrCreate(string path, AppLogger logger)
    {
        if (!File.Exists(path))
        {
            var defaultConfig = new AppConfig();
            File.WriteAllText(path, defaultConfig.ToToml());
            logger.Info($"Created default config at '{path}'.");
            return defaultConfig;
        }

        try
        {
            var text = File.ReadAllText(path);
            return Parse(text, logger);
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to load config at '{path}', using defaults.", ex);
            return new AppConfig();
        }
    }

    public string ToToml()
    {
        return """
[ui]
panel_width = 430
corner_radius = 20
font_family = "Carbon Plus"
font_size = 14
background_color = "#CC1A202B"
border_color = "#66FFFFFF"
text_color = "#FFF5F8FF"
section_header_color = "#FF9CD4FF"
accent_color = "#FF5FC9FF"
hover_color = "#2FFFFFFF"
pressed_color = "#44FFFFFF"
surface_opacity = 0.9

[behavior]
edge_poll_ms = 80
refresh_interval_ms = 3000
activation_cooldown_ms = 900
edge_trigger_pixels = 1
edge_hold_ms = 180
auto_hide_on_focus_loss = true

[window_filter]
exclude_untitled_windows = true
include_minimized_windows = true
unknown_section_name = "Unknown Desktop"
"""; 
    }

    private static AppConfig Parse(string text, AppLogger logger)
    {
        var parsed = Toml.ToModel(text) as TomlTable;
        if (parsed is null)
        {
            logger.Warn("Config parsed to null model. Falling back to defaults.");
            return new AppConfig();
        }

        var config = new AppConfig();

        var ui = GetTable(parsed, "ui");
        if (ui is not null)
        {
            config.PanelWidth = GetInt(ui, "panel_width", config.PanelWidth);
            config.CornerRadius = GetDouble(ui, "corner_radius", config.CornerRadius);
            config.FontFamily = GetString(ui, "font_family", config.FontFamily);
            config.FontSize = GetDouble(ui, "font_size", config.FontSize);
            config.BackgroundColor = GetString(ui, "background_color", config.BackgroundColor);
            config.BorderColor = GetString(ui, "border_color", config.BorderColor);
            config.TextColor = GetString(ui, "text_color", config.TextColor);
            config.SectionHeaderColor = GetString(ui, "section_header_color", config.SectionHeaderColor);
            config.AccentColor = GetString(ui, "accent_color", config.AccentColor);
            config.HoverColor = GetString(ui, "hover_color", config.HoverColor);
            config.PressedColor = GetString(ui, "pressed_color", config.PressedColor);
            config.SurfaceOpacity = Clamp(GetDouble(ui, "surface_opacity", config.SurfaceOpacity), 0.1, 1.0);
        }

        var behavior = GetTable(parsed, "behavior");
        if (behavior is not null)
        {
            config.EdgePollMs = Math.Max(25, GetInt(behavior, "edge_poll_ms", config.EdgePollMs));
            config.RefreshIntervalMs = Math.Max(500, GetInt(behavior, "refresh_interval_ms", config.RefreshIntervalMs));
            config.ActivationCooldownMs = Math.Max(200, GetInt(behavior, "activation_cooldown_ms", config.ActivationCooldownMs));
            config.EdgeTriggerPixels = Math.Max(1, GetInt(behavior, "edge_trigger_pixels", config.EdgeTriggerPixels));
            config.EdgeHoldMs = Math.Max(0, GetInt(behavior, "edge_hold_ms", config.EdgeHoldMs));
            config.AutoHideOnFocusLoss = GetBool(behavior, "auto_hide_on_focus_loss", config.AutoHideOnFocusLoss);
        }

        var windowFilter = GetTable(parsed, "window_filter");
        if (windowFilter is not null)
        {
            config.ExcludeUntitledWindows = GetBool(windowFilter, "exclude_untitled_windows", config.ExcludeUntitledWindows);
            config.IncludeMinimizedWindows = GetBool(windowFilter, "include_minimized_windows", config.IncludeMinimizedWindows);
            config.UnknownSectionName = GetString(windowFilter, "unknown_section_name", config.UnknownSectionName);
        }

        return config;
    }

    private static TomlTable? GetTable(TomlTable table, string key)
    {
        return table.TryGetValue(key, out var value) && value is TomlTable tomlTable ? tomlTable : null;
    }

    private static string GetString(TomlTable table, string key, string fallback)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value as string ?? fallback;
    }

    private static int GetInt(TomlTable table, string key, int fallback)
    {
        if (!table.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            double doubleValue => (int)Math.Round(doubleValue),
            _ => fallback,
        };
    }

    private static double GetDouble(TomlTable table, string key, double fallback)
    {
        if (!table.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => longValue,
            double doubleValue => doubleValue,
            _ => fallback,
        };
    }

    private static bool GetBool(TomlTable table, string key, bool fallback)
    {
        if (!table.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value is bool boolValue ? boolValue : fallback;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}
