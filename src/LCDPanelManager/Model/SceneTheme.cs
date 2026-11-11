using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace LCDPanelManager.Model;

/// <summary>A look for the strip: backdrop and the colors widgets draw with.</summary>
public sealed class SceneTheme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Kind { get; init; }
    public required string Blurb { get; init; }
    public required Brush Text { get; init; }
    public required Brush TextDim { get; init; }
    public required Brush Card { get; init; }
    public required Brush Track { get; init; }
    public required Brush Accent1 { get; init; }
    public required Brush Accent2 { get; init; }
    public required Brush Accent3 { get; init; }
    public required Brush Swatch { get; init; }
    public required Func<FrameworkElement> Backdrop { get; init; }

    private static SolidColorBrush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush G(params string[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.6, 1) };
        for (var i = 0; i < stops.Length; i++)
            g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops[i]), i / (double)(stops.Length - 1)));
        g.Freeze();
        return g;
    }

    private static Ellipse Glow(string hex, double x, double y, double w, double h)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var rg = new RadialGradientBrush(c, Color.FromArgb(0, c.R, c.G, c.B));
        var e = new Ellipse { Width = w, Height = h, Fill = rg, IsHitTestVisible = false };
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }

    private static Path Swirl(string data, string hex, double thickness)
    {
        return new Path
        {
            Data = Geometry.Parse(data),
            Stroke = B(hex),
            StrokeThickness = thickness,
            IsHitTestVisible = false,
            Effect = new BlurEffect { Radius = 3 },
        };
    }

    private static FrameworkElement Layered(Brush baseBrush, params UIElement[] layers)
    {
        var canvas = new Canvas { Width = StripLayout.StripWidth, Height = StripLayout.StripHeight, Background = baseBrush, ClipToBounds = true };
        foreach (var l in layers) canvas.Children.Add(l);
        return canvas;
    }

    private static FrameworkElement AuroraBackdrop()
    {
        var image = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/assets/abstract-aurora.png", UriKind.Absolute));
        image.Freeze();
        return Layered(Brushes.Black, new Image
        {
            Width = StripLayout.StripWidth,
            Height = StripLayout.StripHeight,
            Source = image,
            Stretch = Stretch.UniformToFill,
            IsHitTestVisible = false,
        });
    }

    public static readonly IReadOnlyList<SceneTheme> All = new[]
    {
        new SceneTheme
        {
            Id = "aurora", Name = "Aurora", Title = "Aurora Dashboard", Kind = "Dashboard",
            Blurb = "A little atmosphere. All the numbers that matter. Your hardware, rendered live on the case display.",
            Text = B("#F4F2FF"), TextDim = B("#E4E8F2"), Card = B("#A0050B1D"), Track = B("#40FFFFFF"),
            Accent1 = B("#5CE1FF"), Accent2 = B("#E58CFF"), Accent3 = B("#B9A2FF"),
            Swatch = G("#2B1066", "#6B2FD0", "#18B6E8"),
            Backdrop = AuroraBackdrop,
        },
        new SceneTheme
        {
            Id = "monochrome", Name = "Monochrome", Title = "Monochrome", Kind = "Minimal",
            Blurb = "Quiet grays, big numbers. Reads well from across the room.",
            Text = B("#1D1F23"), TextDim = B("#6B6F78"), Card = B("#14000000"), Track = B("#D3D4D0"),
            Accent1 = B("#3A3D44"), Accent2 = B("#6E727B"), Accent3 = B("#9A9EA6"),
            Swatch = G("#F4F4F0", "#D9DAD5"),
            Backdrop = () => Layered(G("#F3F3EF", "#E4E5E0")),
        },
        new SceneTheme
        {
            Id = "solar", Name = "Solar flare", Title = "Solar Flare", Kind = "Concept",
            Blurb = "Hot gradients for a build that runs warm.",
            Text = B("#FFF4E8"), TextDim = B("#F2C9A0"), Card = B("#33000000"), Track = B("#5A2410"),
            Accent1 = B("#FFD166"), Accent2 = B("#FF7A3D"), Accent3 = B("#FFB38A"),
            Swatch = G("#3A0D05", "#C2410C", "#F59E0B"),
            Backdrop = () => Layered(G("#2A0905", "#8A230A", "#D2540F", "#F29E1F"),
                Glow("#FFB347", -200, 200, 700, 700), Glow("#FF3D00", 40, 1100, 600, 600)),
        },
        new SceneTheme
        {
            Id = "dashboard", Name = "Dashboard", Title = "Dashboard", Kind = "Dashboard",
            Blurb = "Soft dusk colors with amber and blue gauges.",
            Text = B("#F6F4EE"), TextDim = B("#D7D0C0"), Card = B("#22000000"), Track = B("#2A2D35"),
            Accent1 = B("#5C8FFF"), Accent2 = B("#F0C860"), Accent3 = B("#5CE1FF"),
            Swatch = G("#0B1030", "#1E2A60"),
            Backdrop = () => Layered(G("#3C5EA6", "#7B6B5A", "#5A7F7A", "#3F8EA3"),
                Glow("#88C8E0FF", -60, 60, 560, 520), Glow("#66A8D8A0", -80, 620, 600, 640),
                Glow("#66A8D8A0", -60, 1380, 560, 560)),
        },
    };

    public static SceneTheme Get(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
