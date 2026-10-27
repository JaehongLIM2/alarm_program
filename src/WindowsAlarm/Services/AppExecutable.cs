using System.IO;
using System.Reflection;

namespace WindowsAlarm.Services;

internal static class AppExecutable
{
    // Assembly.Location is empty in single-file apps. Relaunch the user's original EXE,
    // including a renamed copy, rather than an extracted assembly or the test runner.
    public static string GetPath() => Resolve(Environment.ProcessPath,
        Assembly.GetEntryAssembly() == typeof(App).Assembly, AppContext.BaseDirectory);

    internal static string Resolve(string? processPath, bool entryIsApp, string baseDirectory)
    {
        if (entryIsApp && !string.IsNullOrWhiteSpace(processPath) &&
            !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(processPath);
        return Path.Combine(Path.GetFullPath(baseDirectory), "WindowsAlarm.exe");
    }
}
