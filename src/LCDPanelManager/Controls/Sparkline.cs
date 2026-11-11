using System.Windows;
using System.Windows.Media;

namespace LCDPanelManager.Controls;

/// <summary>One or two line series on a shared scale.</summary>
public sealed class Sparkline : FrameworkElement
{
    private IReadOnlyList<double> _a = Array.Empty<double>();
    private IReadOnlyList<double> _b = Array.Empty<double>();

    public Brush BrushA { get; set; } = Brushes.Cyan;
    public Brush BrushB { get; set; } = Brushes.Orange;
    public double StrokeThickness { get; set; } = 2;
    public int Capacity { get; set; } = 60;

    public void SetData(IReadOnlyList<double> a, IReadOnlyList<double>? b = null)
    {
        _a = a.ToArray();
        _b = b?.ToArray() ?? Array.Empty<double>();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var max = Math.Max(0.01, _a.Concat(_b).DefaultIfEmpty(0).Max()) * 1.15;
        Draw(dc, _b, BrushB, max);
        Draw(dc, _a, BrushA, max);
    }

    private void Draw(DrawingContext dc, IReadOnlyList<double> s, Brush brush, double max)
    {
        if (s.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var step = ActualWidth / Math.Max(1, Capacity - 1);
        var x0 = ActualWidth - step * (s.Count - 1);
        Point P(int i) => new(x0 + i * step, ActualHeight - s[i] / max * ActualHeight);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(0), false, false);
            for (var i = 1; i < s.Count; i++) ctx.LineTo(P(i), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, new Pen(brush, StrokeThickness) { LineJoin = PenLineJoin.Round }, g);
    }
}
