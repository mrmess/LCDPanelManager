using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LCDPanelManager.Controls;

/// <summary>Horizontal progress bar sized by star columns, so it needs no measuring.</summary>
public sealed class Bar : Grid
{
    private readonly ColumnDefinition _a = new();
    private readonly ColumnDefinition _b = new();
    private readonly Border _track = new();
    private readonly Border _fill = new();

    public Bar()
    {
        Height = 6;
        ColumnDefinitions.Add(_a);
        ColumnDefinitions.Add(_b);
        SetColumnSpan(_track, 2);
        Children.Add(_track);
        Children.Add(_fill);
        Radius = 3;
        Track = new SolidColorBrush(Color.FromRgb(0x26, 0x29, 0x2F));
        Fill = Brushes.White;
        Percent = 0;
    }

    public Brush Fill { get => _fill.Background; set => _fill.Background = value; }
    public Brush Track { get => _track.Background; set => _track.Background = value; }

    public double Radius
    {
        get => _fill.CornerRadius.TopLeft;
        set
        {
            _fill.CornerRadius = new CornerRadius(value);
            _track.CornerRadius = new CornerRadius(value);
        }
    }

    private double _percent;

    public double Percent
    {
        get => _percent;
        set
        {
            _percent = Math.Clamp(value, 0, 100);
            _a.Width = new GridLength(Math.Max(_percent, 0.0001), GridUnitType.Star);
            _b.Width = new GridLength(Math.Max(100 - _percent, 0.0001), GridUnitType.Star);
        }
    }
}
