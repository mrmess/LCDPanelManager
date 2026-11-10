using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StripView.Model;

/// <summary>The scene: a theme plus the widgets placed on the strip. Saved locally only.</summary>
public sealed class StripLayout : INotifyPropertyChanged
{
    public const double StripWidth = 440;
    public const double StripHeight = 1920;

    private string _themeId = "aurora";

    public string ThemeId
    {
        get => _themeId;
        set
        {
            if (_themeId == value) return;
            _themeId = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeId)));
        }
    }

    public ObservableCollection<WidgetItem> Widgets { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StripView", "layout.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static StripLayout CreateDefault()
    {
        var l = new StripLayout { ThemeId = "aurora" };
        l.ResetToDefault();
        return l;
    }

    public void ResetToDefault()
    {
        Widgets.Clear();
        Widgets.Add(WidgetItem.Create(WidgetKind.Clock, 40));
        Widgets.Add(WidgetItem.Create(WidgetKind.Cpu, 260));
        Widgets.Add(WidgetItem.Create(WidgetKind.Gpu, 660));
        Widgets.Add(WidgetItem.Create(WidgetKind.Memory, 1060));
        Widgets.Add(WidgetItem.Create(WidgetKind.Network, 1210));
        Widgets.Add(WidgetItem.Create(WidgetKind.Drive, 1490, "C:"));
        Widgets.Add(WidgetItem.Create(WidgetKind.Drive, 1640, "D:"));
        Widgets.Add(WidgetItem.Create(WidgetKind.Text, 1790));
    }

    public static StripLayout LoadOrDefault()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var loaded = JsonSerializer.Deserialize<StripLayout>(File.ReadAllText(StorePath), Json);
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // Corrupt or old file: fall back to the default scene.
        }
        return CreateDefault();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(this, Json));
    }
}
