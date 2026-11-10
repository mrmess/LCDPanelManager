using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StripView.Model;

namespace StripView.Controls;

/// <summary>How one widget looks on the strip. All sizes are strip pixels (440 wide).</summary>
public sealed class WidgetVisual : Grid
{
    private readonly SceneTheme _theme;
    private readonly Border _outline;
    private Action<TelemetrySnapshot>? _update;

    public WidgetItem Item { get; }
    public Button DeleteButton { get; }

    public WidgetVisual(WidgetItem item, SceneTheme theme, bool editable)
    {
        Item = item;
        _theme = theme;
        Background = Brushes.Transparent; // hit-testable for drag-to-move
        Children.Add(Build());

        _outline = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5C, 0xE1, 0xFF)),
            BorderThickness = new Thickness(5),
            CornerRadius = new CornerRadius(26),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        Children.Add(_outline);

        DeleteButton = new Button
        {
            Content = new TextBlock { Text = "✕", FontSize = 26, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center },
            Width = 52,
            Height = 52,
            Margin = new Thickness(0, -14, -14, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromRgb(0x5C, 0xE1, 0xFF)),
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed,
            ToolTip = "Remove from strip",
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        DeleteButton.Template = RoundTemplate();
        if (editable) Children.Add(DeleteButton);
    }

    public bool IsSelected
    {
        set
        {
            _outline.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            DeleteButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void Update(TelemetrySnapshot t) => _update?.Invoke(t);

    public void Rebuild()
    {
        Children.RemoveAt(0);
        Children.Insert(0, Build());
    }

    private static ControlTemplate RoundTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var b = new FrameworkElementFactory(typeof(Border));
        b.SetValue(Border.CornerRadiusProperty, new CornerRadius(26));
        b.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        b.AppendChild(cp);
        t.VisualTree = b;
        return t;
    }

    private TextBlock Tb(string text, double size, Brush? brush = null, FontWeight? weight = null, HorizontalAlignment align = HorizontalAlignment.Left) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush ?? _theme.Text,
        FontWeight = weight ?? FontWeights.Normal,
        HorizontalAlignment = align,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
    };

    private Border Card(UIElement child, double pad = 26) => new()
    {
        Background = _theme.Card,
        CornerRadius = new CornerRadius(26),
        Padding = new Thickness(pad),
        Child = child,
    };

    private FrameworkElement Build()
    {
        _update = null;
        return Item.Kind switch
        {
            WidgetKind.Cpu => Ring("CPU", _theme.Accent1, t => (t.CpuPercent, TelemetrySource.FormatTemperature(t.CpuTempC) + " temp")),
            WidgetKind.Gpu => Ring("GPU", _theme.Accent2, t => (t.GpuPercent, TelemetrySource.FormatTemperature(t.GpuTempC) + " temp")),
            WidgetKind.GpuTemp => BigNumber("GPU TEMP", t => TelemetrySource.FormatTemperature(t.GpuTempC)),
            WidgetKind.Memory => BarRow("MEMORY", _theme.Accent1, t => (t.MemPercent, $"{t.MemPercent:0}%", $"{t.MemUsedGb:0.0} / {t.MemTotalGb:0} GB")),
            WidgetKind.Drive => BarRow(Item.Text.Length > 0 ? Item.Text.ToUpperInvariant() : "C:", _theme.Accent3, t =>
            {
                var d = t.Drive(Item.Text);
                return d is null ? (0d, "—", "no drive") : (d.UsedPercent, $"{d.UsedPercent:0}%", d.FreeText);
            }),
            WidgetKind.Network => Network(),
            WidgetKind.Clock => Clock(),
            WidgetKind.Text => TextWidget(),
            WidgetKind.Image => ImageWidget(),
            _ => new Grid(),
        };
    }

    private FrameworkElement Ring(string label, Brush accent, Func<TelemetrySnapshot, (double? Value, string Sub)> read)
    {
        var size = Math.Min(Item.Width, Item.Height) - 70;
        var ring = new RingGauge { Width = size, Height = size, Thickness = Math.Max(14, size / 11), ArcBrush = accent, TrackBrush = _theme.Track };
        var value = Tb("0%", size / 4.6, weight: FontWeights.SemiBold, align: HorizontalAlignment.Center);
        var sub = Tb("", size / 13, _theme.TextDim, align: HorizontalAlignment.Center);
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        center.Children.Add(value);
        center.Children.Add(Tb(label, size / 11, _theme.TextDim, FontWeights.SemiBold, HorizontalAlignment.Center));
        center.Children.Add(sub);
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(ring);
        g.Children.Add(center);
        _update = t =>
        {
            var (v, s) = read(t);
            ring.Value = v ?? 0;
            ring.Opacity = v.HasValue ? 1 : 0.35;
            value.Text = v.HasValue ? $"{v:0}%" : "n/a";
            sub.Text = s;
        };
        return Card(g, 10);
    }

    private FrameworkElement BigNumber(string label, Func<TelemetrySnapshot, string> read)
    {
        var value = Tb("—", Math.Min(140, Item.Height * 0.55), weight: FontWeights.SemiBold, align: HorizontalAlignment.Center);
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(value);
        s.Children.Add(Tb(label, 26, _theme.TextDim, FontWeights.SemiBold, HorizontalAlignment.Center));
        _update = t => value.Text = read(t);
        return Card(s);
    }

    private FrameworkElement BarRow(string label, Brush accent, Func<TelemetrySnapshot, (double Pct, string Value, string Sub)> read)
    {
        var head = new DockPanel();
        var value = Tb("—", 30, weight: FontWeights.SemiBold);
        DockPanel.SetDock(value, Dock.Right);
        head.Children.Add(value);
        head.Children.Add(Tb(label, 26, _theme.TextDim, FontWeights.SemiBold));
        var bar = new Bar { Height = 18, Radius = 9, Fill = accent, Track = _theme.Track, Margin = new Thickness(0, 12, 0, 8) };
        var sub = Tb("", 22, _theme.TextDim);
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(head);
        s.Children.Add(bar);
        s.Children.Add(sub);
        _update = t =>
        {
            var (p, v, x) = read(t);
            bar.Percent = p;
            value.Text = v;
            sub.Text = x;
        };
        return Card(s, 22);
    }

    private FrameworkElement Network()
    {
        var down = Tb("0", 30, weight: FontWeights.SemiBold);
        var up = Tb("0", 30, weight: FontWeights.SemiBold);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var d = new StackPanel();
        d.Children.Add(Tb("DOWN Mb/s", 20, _theme.TextDim, FontWeights.SemiBold));
        d.Children.Add(down);
        var u = new StackPanel();
        u.Children.Add(Tb("UP Mb/s", 20, _theme.TextDim, FontWeights.SemiBold));
        u.Children.Add(up);
        SetColumn(u, 1);
        row.Children.Add(d);
        row.Children.Add(u);
        var spark = new Sparkline { BrushA = _theme.Accent1, BrushB = _theme.Accent2, StrokeThickness = 4, Margin = new Thickness(0, 14, 0, 0) };
        var dock = new DockPanel();
        DockPanel.SetDock(row, Dock.Top);
        dock.Children.Add(row);
        dock.Children.Add(spark);
        _update = t =>
        {
            down.Text = TelemetrySource.FormatMbps(t.DownMbps);
            up.Text = TelemetrySource.FormatMbps(t.UpMbps);
            spark.SetData(t.DownHistory, t.UpHistory);
        };
        return Card(dock, 22);
    }

    private FrameworkElement Clock()
    {
        var time = Tb("", Math.Min(120, Item.Height * 0.55), weight: FontWeights.SemiBold, align: HorizontalAlignment.Center);
        var date = Tb("", 28, _theme.TextDim, align: HorizontalAlignment.Center);
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(time);
        s.Children.Add(date);
        _update = _ =>
        {
            var now = DateTime.Now;
            time.Text = now.ToString("HH:mm");
            date.Text = now.ToString("ddd d MMM");
        };
        _update(null!);
        return s;
    }

    private FrameworkElement TextWidget()
    {
        var tb = Tb(Item.Text, 44, weight: FontWeights.SemiBold, align: HorizontalAlignment.Center);
        tb.TextWrapping = TextWrapping.Wrap;
        tb.TextAlignment = TextAlignment.Center;
        tb.VerticalAlignment = VerticalAlignment.Center;
        return tb;
    }

    private FrameworkElement ImageWidget()
    {
        if (!string.IsNullOrWhiteSpace(Item.ImagePath) && File.Exists(Item.ImagePath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(Item.ImagePath);
                bmp.DecodePixelWidth = 440;
                bmp.EndInit();
                bmp.Freeze();
                return new Border
                {
                    CornerRadius = new CornerRadius(26),
                    Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill },
                };
            }
            catch
            {
                // Fall through to the placeholder.
            }
        }
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(Tb("IMAGE", 30, _theme.TextDim, FontWeights.SemiBold, HorizontalAlignment.Center));
        s.Children.Add(Tb("Choose a file in Design", 22, _theme.TextDim, align: HorizontalAlignment.Center));
        return new Border
        {
            CornerRadius = new CornerRadius(26),
            BorderBrush = _theme.TextDim,
            BorderThickness = new Thickness(3),
            Background = _theme.Card,
            Child = s,
        };
    }
}
