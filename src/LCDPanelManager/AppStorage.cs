using System.IO;

namespace LCDPanelManager;

/// <summary>
/// Per-user data under %LOCALAPPDATA%\LCDPanelManager.
/// On first run, if that folder does not exist and %LOCALAPPDATA%\StripView does,
/// the old folder is moved so existing layouts, preferences, and traces are kept.
/// </summary>
public static class AppStorage
{
    public const string FolderName = "LCDPanelManager";
    public const string LegacyFolderName = "StripView";

    private static readonly object Gate = new();
    private static bool _ready;

    private static string NewRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    private static string LegacyRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyFolderName);

    public static string Root
    {
        get
        {
            EnsureMigrated();
            return NewRoot;
        }
    }

    public static void EnsureMigrated()
    {
        lock (Gate)
        {
            if (_ready) return;
            try
            {
                if (!Directory.Exists(NewRoot) && Directory.Exists(LegacyRoot))
                {
                    try
                    {
                        Directory.Move(LegacyRoot, NewRoot);
                    }
                    catch
                    {
                        CopyDirectory(LegacyRoot, NewRoot);
                    }
                }
            }
            catch
            {
                // Leave both folders as-is; callers still write to the new path.
            }
            _ready = true;
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
