using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace LCDPanelManager.Model;

public sealed class AppPreferences
{
    public bool StartMinimized { get; set; }
    public bool StartWithWindows { get; set; }

    private static string StorePath => Path.Combine(AppStorage.Root, "preferences.json");

    public static AppPreferences Load()
    {
        try
        {
            if (File.Exists(StorePath))
                return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(StorePath)) ?? new AppPreferences();
        }
        catch { }
        return new AppPreferences();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LCDPanelManager";
    private const string LegacyValueName = "StripView";

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{AppPaths.Executable}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Drop the pre-rename Run entry so it cannot keep launching the old exe.</summary>
    public static void RemoveLegacy()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }
}
