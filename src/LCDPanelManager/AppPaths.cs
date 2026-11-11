using System.IO;

namespace LCDPanelManager;

/// <summary>
/// Location of this app on the current machine. Never a baked-in drive or user path.
/// </summary>
public static class AppPaths
{
    public const string ExecutableFileName = "LCDPanelManager.exe";

    public static string Executable
    {
        get
        {
            var process = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(process) &&
                string.Equals(Path.GetFileName(process), ExecutableFileName, StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(process);

            var candidate = Path.Combine(AppContext.BaseDirectory, ExecutableFileName);
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : (process ?? candidate);
        }
    }
}
