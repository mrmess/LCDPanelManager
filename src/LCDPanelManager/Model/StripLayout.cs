using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LCDPanelManager.Model;

/// <summary>The scene: a theme plus the widgets placed on the strip. Saved locally only.</summary>
public sealed class StripLayout : INotifyPropertyChanged
{
    public const double StripWidth = 440;
    public const double StripHeight = 1920;
    public const int CurrentVersion = 2;

    private string _themeId = "aurora";

    public int Version { get; set; }

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

    public static string StorePath => Path.Combine(AppStorage.Root, "layout.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static StripLayout CreateDefault()
    {
        var layout = new StripLayout { ThemeId = "aurora" };
        layout.ResetToDefault();
        return layout;
    }

    public void ResetToDefault()
    {
        Version = CurrentVersion;
        ThemeId = "aurora";
        Widgets.Clear();
        Add(WidgetKind.Cpu, 90, 330);
        Add(WidgetKind.Gpu, 450, 330);
        Add(WidgetKind.Memory, 810, 150);
        Add(WidgetKind.Network, 980, 290);

        var y = 1300.0;
        var letters = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    letters.Add(drive.Name.TrimEnd('\\'));
                }
                catch { }
            }
        }
        catch { }
        if (letters.Count == 0) letters.Add("C:");
        foreach (var letter in letters)
        {
            if (y + 150 > StripHeight) break;
            Add(WidgetKind.Drive, y, 150, letter);
            y += 160;
        }
    }

    private void Add(WidgetKind kind, double y, double height, string text = "")
    {
        var item = WidgetItem.Create(kind, y, text);
        item.Height = height;
        Widgets.Add(item);
    }

    public static StripLayout LoadOrDefault()
    {
        try
        {
            if (!File.Exists(StorePath)) return CreateDefault();
            var loaded = JsonSerializer.Deserialize<StripLayout>(File.ReadAllText(StorePath), Json);
            if (loaded is null) return CreateDefault();

            if (loaded.Version < CurrentVersion)
            {
                try { File.Copy(StorePath, StorePath + ".v1.bak", overwrite: false); } catch { }
            }
            if (string.IsNullOrWhiteSpace(loaded.ThemeId)) loaded.ThemeId = "aurora";
            loaded.Widgets ??= new ObservableCollection<WidgetItem>();
            foreach (var widget in loaded.Widgets)
            {
                if (widget.Id == Guid.Empty) widget.Id = Guid.NewGuid();
                if (widget.Width <= 0) widget.Width = 400;
                if (widget.Height <= 0) widget.Height = WidgetCatalog.Get(widget.Kind).DefaultHeight;
                widget.Text ??= "";
            }
            return loaded;
        }
        catch
        {
            try
            {
                if (File.Exists(StorePath))
                {
                    var backup = StorePath + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                    File.Move(StorePath, backup);
                }
            }
            catch { }
            return CreateDefault();
        }
    }

    public void Save()
    {
        Version = CurrentVersion;
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(this, Json));
    }
}
