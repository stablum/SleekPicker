using System.Runtime.InteropServices;
using System.Linq;
using Microsoft.Win32;

namespace SleekPicker.App;

internal sealed class VirtualDesktopService
{
    private const string VirtualDesktopsRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const string DesktopsRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops";

    private readonly AppLogger _logger;
    private readonly IVirtualDesktopManager? _manager;

    public VirtualDesktopService(AppLogger logger)
    {
        _logger = logger;

        try
        {
            _manager = (IVirtualDesktopManager)new VirtualDesktopManagerComObject();
        }
        catch (Exception ex)
        {
            _logger.Warn($"VirtualDesktopManager COM unavailable: {ex.Message}");
        }
    }

    public DesktopSnapshot GetDesktopSnapshot()
    {
        var orderedIds = ReadOrderedDesktopIds();
        var names = ReadDesktopNames();

        if (orderedIds.Count == 0)
        {
            orderedIds.AddRange(names.Keys);
        }

        var desktops = new List<DesktopMetadata>(orderedIds.Count);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var desktopId = orderedIds[i];
            var desktopName = names.TryGetValue(desktopId, out var knownName) && !string.IsNullOrWhiteSpace(knownName)
                ? knownName
                : $"Desktop {i + 1}";

            desktops.Add(new DesktopMetadata(desktopId, desktopName, i));
        }

        var currentDesktopId = ReadCurrentDesktopId();
        if (currentDesktopId is null && desktops.Count > 0)
        {
            currentDesktopId = desktops[0].Id;
        }

        return new DesktopSnapshot(desktops, currentDesktopId);
    }

    public Guid? TryGetWindowDesktopId(IntPtr hWnd)
    {
        if (_manager is null)
        {
            return null;
        }

        try
        {
            return _manager.GetWindowDesktopId(hWnd, out var desktopId) == 0
                ? desktopId
                : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SwitchToDesktopAsync(Guid targetDesktopId, CancellationToken cancellationToken = default)
    {
        var snapshot = GetDesktopSnapshot();
        if (snapshot.Desktops.Count == 0)
        {
            return false;
        }

        var targetIndex = snapshot.IndexOf(targetDesktopId);
        if (targetIndex < 0)
        {
            return false;
        }

        var currentDesktopId = snapshot.CurrentDesktopId;
        if (!currentDesktopId.HasValue)
        {
            return false;
        }

        var currentIndex = snapshot.IndexOf(currentDesktopId.Value);
        if (currentIndex < 0)
        {
            return false;
        }

        if (currentIndex == targetIndex)
        {
            return true;
        }

        var moveRight = targetIndex > currentIndex;
        var totalSteps = Math.Abs(targetIndex - currentIndex);

        for (var i = 0; i < totalSteps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeMethods.SendVirtualDesktopSwitch(moveRight);
            await Task.Delay(130, cancellationToken);
        }

        return true;
    }

    private static List<Guid> ReadOrderedDesktopIds()
    {
        using var key = Registry.CurrentUser.OpenSubKey(VirtualDesktopsRegistryPath);
        if (key?.GetValue("VirtualDesktopIDs") is not byte[] desktopIdBuffer || desktopIdBuffer.Length < 16)
        {
            return new List<Guid>();
        }

        return ParseGuidArray(desktopIdBuffer);
    }

    private static Guid? ReadCurrentDesktopId()
    {
        using var key = Registry.CurrentUser.OpenSubKey(VirtualDesktopsRegistryPath);
        if (key is null)
        {
            return null;
        }

        var raw = key.GetValue("CurrentVirtualDesktop");
        return raw switch
        {
            byte[] bytes when bytes.Length >= 16 => new Guid(bytes[..16]),
            string text when Guid.TryParse(text, out var parsed) => parsed,
            _ => null,
        };
    }

    private static Dictionary<Guid, string> ReadDesktopNames()
    {
        var result = new Dictionary<Guid, string>();

        using var baseKey = Registry.CurrentUser.OpenSubKey(DesktopsRegistryPath);
        if (baseKey is null)
        {
            return result;
        }

        var subKeyNames = baseKey.GetSubKeyNames();
        foreach (var subKeyName in subKeyNames)
        {
            if (!TryParseDesktopKeyGuid(subKeyName, out var desktopId))
            {
                continue;
            }

            using var desktopKey = baseKey.OpenSubKey(subKeyName);
            if (desktopKey?.GetValue("Name") is string desktopName)
            {
                result[desktopId] = desktopName.Trim();
            }
        }

        return result;
    }

    private static List<Guid> ParseGuidArray(byte[] buffer)
    {
        var result = new List<Guid>(buffer.Length / 16);

        for (var index = 0; index <= buffer.Length - 16; index += 16)
        {
            var guidBytes = new byte[16];
            Buffer.BlockCopy(buffer, index, guidBytes, 0, 16);
            result.Add(new Guid(guidBytes));
        }

        return result;
    }

    private static bool TryParseDesktopKeyGuid(string text, out Guid desktopId)
    {
        if (Guid.TryParse(text, out desktopId))
        {
            return true;
        }

        if (Guid.TryParse(text.Trim('{', '}'), out desktopId))
        {
            return true;
        }

        desktopId = Guid.Empty;
        return false;
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);

        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

        int MoveWindowToDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.LPStruct)] Guid desktopId);
    }

    [ComImport]
    [Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    private class VirtualDesktopManagerComObject;
}

internal sealed record DesktopMetadata(Guid Id, string Name, int Index);

internal sealed class DesktopSnapshot
{
    public DesktopSnapshot(IReadOnlyList<DesktopMetadata> desktops, Guid? currentDesktopId)
    {
        Desktops = desktops;
        CurrentDesktopId = currentDesktopId;

        NameById = desktops
            .GroupBy(desktop => desktop.Id)
            .ToDictionary(group => group.Key, group => group.First().Name);
    }

    public IReadOnlyList<DesktopMetadata> Desktops { get; }

    public IReadOnlyDictionary<Guid, string> NameById { get; }

    public Guid? CurrentDesktopId { get; }

    public int IndexOf(Guid desktopId)
    {
        for (var i = 0; i < Desktops.Count; i++)
        {
            if (Desktops[i].Id == desktopId)
            {
                return i;
            }
        }

        return -1;
    }
}
