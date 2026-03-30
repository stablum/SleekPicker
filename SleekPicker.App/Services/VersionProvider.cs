using System.Reflection;

namespace SleekPicker.App;

internal static class VersionProvider
{
    private const string DefaultVersion = "0.0.0";

    public static string GetVersionLabel()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? DefaultVersion;
    }
}
