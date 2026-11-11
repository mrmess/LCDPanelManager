using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LCDPanelManager.Model;

/// <summary>One widget placed on the 440x1920 strip. Coordinates are strip pixels.</summary>
public sealed class WidgetItem : INotifyPropertyChanged
{
    private double _x = 20, _y, _width = 400, _height = 200;
    private string _text = "";
    private string? _imagePath;

    public Guid Id { get; set; } = Guid.NewGuid();
    public WidgetKind Kind { get; set; }

    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set { if (Set(ref _y, value)) OnChanged(nameof(Summary)); } }
    public double Width { get => _width; set => Set(ref _width, value); }
    public double Height { get => _height; set { if (Set(ref _height, value)) OnChanged(nameof(Summary)); } }
    public string Text { get => _text; set { if (Set(ref _text, value)) OnChanged(nameof(Summary)); } }
    public string? ImagePath { get => _imagePath; set => Set(ref _imagePath, value); }

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var label = WidgetCatalog.Get(Kind).Label;
            if (Kind is WidgetKind.Text or WidgetKind.Drive && !string.IsNullOrWhiteSpace(Text))
                label += $" · {Text}";
            return $"{label}   y {Y:0} · h {Height:0}";
        }
    }

    public static WidgetItem Create(WidgetKind kind, double y, string text = "") => new()
    {
        Kind = kind,
        Y = y,
        Height = WidgetCatalog.Get(kind).DefaultHeight,
        Text = text.Length > 0 ? text : kind switch
        {
            WidgetKind.Text => "Text",
            WidgetKind.Drive => "C:",
            _ => "",
        },
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
