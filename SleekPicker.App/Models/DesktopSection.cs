using System.Collections.ObjectModel;

namespace SleekPicker.App;

internal sealed class DesktopSection
{
    public DesktopSection(string name, IEnumerable<WindowEntry> windows)
    {
        Name = name;
        Windows = new ObservableCollection<WindowEntry>(windows);
    }

    public string Name { get; }

    public ObservableCollection<WindowEntry> Windows { get; }
}
