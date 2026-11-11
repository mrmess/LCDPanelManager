using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace StripView.Model;

public sealed class AppPreferences
{
    public bool StartMinimized { get; set; }
    public bool StartWithWindows { get; set; }

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StripView", "preferences.json");

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
    private const string ValueName = "StripView";

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "StripView.exe");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
