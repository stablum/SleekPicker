# SleekPicker

SleekPicker is a Windows 11 side-panel window picker.

It runs in the background and opens when the mouse hits the right edge of the screen. The panel lists open windows grouped by virtual desktop name, shows an icon for each window, and lets you click any entry to switch to the right desktop and focus that window.

Current version: `1.0.8`

## Features

- Right-edge activation with slide-in panel animation and edge-hold delay to reduce accidental popups
- Edge activation rearms only after cursor leaves the edge trigger zone
- Window list grouped by virtual desktop section in desktop-order sequence
- App icons shown beside each window title
- Click-to-focus behavior (switch desktop + bring window to foreground)
- Click-to-focus preserves maximized windows (restore is only used for minimized targets)
- Keyboard shortcuts per listed window: `1`..`9`, `0`, then `A`..`Z` for quick activation
- Per-window `X` button to request closing the selected window
- Panel auto-hides when pointer leaves it (including fast cursor movement fallback)
- Panel hides when clicking non-interactive/unused panel area
- Semi-transparent, rounded panel with blur attempt on supported systems
- Tray icon with quick actions (`Open Panel`, `Refresh`, `Exit`)
- Configurable UI and behavior through `config.toml`
- Runtime logging to `SleekPicker.log` in the repo root
- Fallback window scan when strict filtering returns no results

## Requirements

- Windows 11 (or Windows 10 20H1+)
- .NET 8 Runtime (required to run)
- .NET SDK 8.0+ (required to build from source)
- `winget` (required only if you want installer buttons to auto-install .NET)

## Build

```powershell
dotnet build .\SleekPicker.sln -c Release
```

## Run

```powershell
dotnet run --project .\SleekPicker.App\SleekPicker.App.csproj
```

## Restart

Use the helper script from the repo root:

```powershell
.\restart-sleekpicker.ps1
```

If you want to force a rebuild first:

```powershell
.\restart-sleekpicker.ps1 -Rebuild
```

The script launches via `dotnet SleekPicker.App.dll` with a hidden host window for faster startup and no visible empty terminal.
## Archive Package

Create a zip archive of the full repository (including built executables) using:

```powershell
.\package-sleekpicker.ps1
```

The script creates `SleekPicker-<version>.zip` in the repo root and excludes existing `.zip` files from the archive content.
It also publishes a standalone `setup.exe` launcher into the repo root before creating the archive.
To keep package size lower, it excludes build intermediates and redundant setup runtime outputs (for example `obj/`, setup publish temp files, and app debug binaries).

## Setup Launcher (`setup.exe`)

`setup.exe` now provides a native WinForms installer UI and does not launch `powershell.exe`.
Use this path on machines where AV/policy flags script-based installers.
It targets `.NET Framework 4.8` (included with Windows 11), so it does not bundle the .NET 8 runtime.

Build it from source:

```powershell
.\build-setup-exe.ps1
```

Then run:

```powershell
.\setup.exe
```

The native setup UI includes the same core installer actions:

- Shows shared app version from `version.txt`
- Shows/install/removes autorun entry in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SleekPicker`
- Starts/stops SleekPicker
- Checks `.NET 8 Runtime` and `.NET SDK (8+)`
- Installs `.NET 8 Runtime` and `.NET 8 SDK` through `winget`

`installer-gui.ps1` is still available, but `setup.exe` no longer depends on it.

## Installer GUI

Use the PowerShell installer GUI to set or remove startup autorun in the current user session:

```powershell
.\installer-gui.ps1
```

The installer:

- Shows the current shared app version from `version.txt`
- Shows whether the autorun entry is installed
- Shows whether .NET 8 Runtime is installed
- Shows whether any .NET SDK `8+` is installed (for example, SDK `10.x` counts as installed)
- Installs autorun by setting `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SleekPicker`
- Removes that autorun entry on uninstall
- Starts SleekPicker immediately via `Start SleekPicker Now` (without waiting for next sign-in)
- Stops any running SleekPicker process via `Stop SleekPicker`
- Installs `.NET 8 Runtime` and `.NET 8 SDK` through dedicated buttons (via `winget`)

## Configuration

Settings live in `config.toml` at the repository root.

```toml
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
```

## Notes

- The default configured font is `Carbon Plus`. If it is not installed, Windows fallback fonts are used.
- If blur cannot be enabled by the OS compositor, the panel still renders with semi-transparent styling.
- Panel placement is DPI-aware; edge activation should align correctly on scaled displays.
- Increase `edge_hold_ms` to require a longer intentional edge hold before showing the panel.
- Use `surface_opacity` (`0.1` to `1.0`) to force stronger or lighter panel translucency independent of blur support.
- The app version shown in UI is embedded at build time from `version.txt` (not read dynamically at runtime).


