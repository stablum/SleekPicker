using System.IO;

namespace SleekPicker.App;

internal static class RuntimePaths
{
    public static string ResolveRepositoryRoot()
    {
        var probes = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        };

        foreach (var probe in probes)
        {
            var result = FindContainingRoot(probe);
            if (result is not null)
            {
                return result;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string? FindContainingRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            var agentsPath = Path.Combine(current.FullName, "AGENTS.md");
            var instructionsPath = Path.Combine(current.FullName, "INITIAL_INSTRUCTIONS.md");
            if (File.Exists(agentsPath) || File.Exists(instructionsPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
