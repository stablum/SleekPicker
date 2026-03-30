using System.Windows.Media;

namespace SleekPicker.App;

internal sealed class WindowEntry
{
    public required IntPtr Handle { get; init; }

    public required string Title { get; init; }

    public required string ProcessName { get; init; }

    public required string DesktopName { get; init; }

    public Guid? DesktopId { get; init; }

    public ImageSource? Icon { get; init; }

    public string? ShortcutLabel { get; set; }
}
