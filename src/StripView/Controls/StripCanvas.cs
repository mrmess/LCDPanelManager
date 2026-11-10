using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StripView.Model;

namespace StripView.Controls;

/// <summary>
/// Renders a <see cref="StripLayout"/> at the real 440x1920 aspect, scaled to fit.
/// When <see cref="IsEditable"/> is true it is also the drop target for the Design tab:
/// drop a widget kind to place it, drag a placed widget to move it, Delete or ✕ to remove it.
/// </summary>
public sealed class StripCanvas : Border
{
    public const string DragFormat = "StripView.WidgetKind";
    private const double W = StripLayout.StripWidth;
    private const double H = StripLayout.StripHeight;
    private const double Snap = 10;

    private readonly Grid _surface;
    private readonly Canvas _canvas;
    private readonly Canvas _overlay;
    private readonly Border _ghost;
    private readonly TextBlock _ghostLabel;
    private readonly Dictionary<WidgetItem, WidgetVisual> _visuals = new();

    private StripLayout? _layout;
    private TelemetrySource? _telemetry;
    private SceneTheme _theme = SceneTheme.All[0];
    private WidgetItem? _dragItem;
    private Vector _dragOffset;

    private bool _editable;

    public bool IsEditable
    {
        get => _editable;
        set
        {
            _editable = value;
            AllowDrop = value;
        }
    }

    public WidgetItem? Selected { get; private set; }
    public event EventHandler? SelectionChanged;
    public event EventHandler? LayoutEdited;

    public StripCanvas()
    {
        _canvas = new Canvas { Width = W, Height = H };
        _ghostLabel = new TextBlock { FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _ghost = new Border
        {
            Width = W - 40,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5C, 0xE1, 0xFF)),
            BorderThickness = new Thickness(5),
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x5C, 0xE1, 0xFF)),
            CornerRadius = new CornerRadius(26),
            Child = _ghostLabel,
            Visibility = Visibility.Collapsed,
        };
        _overlay = new Canvas { Width = W, Height = H, IsHitTestVisible = false };
        _overlay.Children.Add(_ghost);
        _surface = new Grid { Width = W, Height = H, ClipToBounds = true, Background = Brushes.Black };
        _surface.Children.Add(new Grid());
        _surface.Children.Add(_canvas);
        _surface.Children.Add(_overlay);
        Child = new Viewbox { Stretch = Stretch.Uniform, Child = _surface };

        // The strip is drawn scaled down; Display-mode text hinting drops large glyphs at small scales.
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Focusable = true;
        FocusVisualStyle = null;
        _surface.MouseLeftButtonDown += (_, e) =>
        {
            if (!IsEditable) return;
            Focus();
            if (e.OriginalSource == _surface || e.OriginalSource == _canvas || !IsInsideWidget(e.OriginalSource as DependencyObject))
                Select(null);
        };
    }

    public void Attach(StripLayout layout, TelemetrySource telemetry)
    {
        _layout = layout;
        _telemetry = telemetry;
        layout.Widgets.CollectionChanged += OnWidgetsChanged;
        layout.PropertyChanged += OnLayoutPropertyChanged;
        ApplyTheme();
    }

    public void Detach()
    {
        if (_layout is null) return;
        _layout.Widgets.CollectionChanged -= OnWidgetsChanged;
        _layout.PropertyChanged -= OnLayoutPropertyChanged;
        foreach (var item in _visuals.Keys) item.PropertyChanged -= OnItemChanged;
        _layout = null;
    }

    public void RefreshValues()
    {
        if (_telemetry is null) return;
        foreach (var v in _visuals.Values) v.Update(_telemetry);
    }

    public void Select(WidgetItem? item)
    {
        if (Selected == item) return;
        if (Selected is not null && _visuals.TryGetValue(Selected, out var old)) old.IsSelected = false;
        Selected = item;
        if (item is not null && _visuals.TryGetValue(item, out var now)) now.IsSelected = true;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Put a new widget on the strip, centred on a strip-pixel Y (snapped, clamped).</summary>
    public WidgetItem? Place(WidgetKind kind, double centerY)
    {
        if (_layout is null) return null;
        var item = WidgetItem.Create(kind, 0);
        item.Y = Clamp(SnapTo(centerY - item.Height / 2), 0, H - item.Height);
        _layout.Widgets.Add(item);
        Select(item);
        LayoutEdited?.Invoke(this, EventArgs.Empty);
        return item;
    }

    /// <summary>Place below the lowest widget (used by double-click in the palette).</summary>
    public WidgetItem? Append(WidgetKind kind)
    {
        if (_layout is null) return null;
        var bottom = _layout.Widgets.Count == 0 ? 20 : _layout.Widgets.Max(w => w.Y + w.Height) + 20;
        var h = WidgetCatalog.Get(kind).DefaultHeight;
        return Place(kind, Math.Min(bottom, H - h) + h / 2);
    }

    public void RemoveSelected()
    {
        if (_layout is null || Selected is null) return;
        var item = Selected;
        Select(null);
        _layout.Widgets.Remove(item);
        LayoutEdited?.Invoke(this, EventArgs.Empty);
    }

    // ---------- rendering ----------

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StripLayout.ThemeId)) ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (_layout is null) return;
        _theme = SceneTheme.Get(_layout.ThemeId);
        _surface.Children.RemoveAt(0);
        _surface.Children.Insert(0, _theme.Backdrop());
        RebuildAll();
    }

    private void OnWidgetsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildAll();

    private void RebuildAll()
    {
        if (_layout is null) return;
        foreach (var item in _visuals.Keys) item.PropertyChanged -= OnItemChanged;
        _visuals.Clear();
        _canvas.Children.Clear();
        foreach (var item in _layout.Widgets)
        {
            var v = new WidgetVisual(item, _theme, IsEditable);
            _visuals[item] = v;
            item.PropertyChanged += OnItemChanged;
            Position(v);
            if (IsEditable) Wire(v);
            _canvas.Children.Add(v);
            if (_telemetry is not null) v.Update(_telemetry);
        }
        if (Selected is not null)
        {
            if (_visuals.TryGetValue(Selected, out var sel)) sel.IsSelected = true;
            else Select(null);
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not WidgetItem item || !_visuals.TryGetValue(item, out var v)) return;
        switch (e.PropertyName)
        {
            case nameof(WidgetItem.X):
            case nameof(WidgetItem.Y):
                Position(v);
                break;
            case nameof(WidgetItem.Width):
            case nameof(WidgetItem.Height):
            case nameof(WidgetItem.Text):
            case nameof(WidgetItem.ImagePath):
                Position(v);
                v.Rebuild();
                if (_telemetry is not null) v.Update(_telemetry);
                break;
        }
    }

    private static void Position(WidgetVisual v)
    {
        v.Width = v.Item.Width;
        v.Height = v.Item.Height;
        Canvas.SetLeft(v, v.Item.X);
        Canvas.SetTop(v, v.Item.Y);
    }

    // ---------- move / delete ----------

    private void Wire(WidgetVisual v)
    {
        v.Cursor = Cursors.SizeAll;
        v.DeleteButton.Click += (_, _) =>
        {
            Select(v.Item);
            RemoveSelected();
        };
        v.MouseLeftButtonDown += (_, e) =>
        {
            if (IsInside(e.OriginalSource as DependencyObject, v.DeleteButton)) return;
            Focus();
            Select(v.Item);
            _dragItem = v.Item;
            _dragOffset = e.GetPosition(_canvas) - new Point(v.Item.X, v.Item.Y);
            v.CaptureMouse();
            e.Handled = true;
        };
        v.MouseMove += (_, e) =>
        {
            if (_dragItem != v.Item || !v.IsMouseCaptured) return;
            var p = e.GetPosition(_canvas) - _dragOffset;
            v.Item.X = Clamp(SnapTo(p.X), 0, W - v.Item.Width);
            v.Item.Y = Clamp(SnapTo(p.Y), 0, H - v.Item.Height);
        };
        v.MouseLeftButtonUp += (_, _) =>
        {
            if (_dragItem != v.Item) return;
            _dragItem = null;
            v.ReleaseMouseCapture();
            LayoutEdited?.Invoke(this, EventArgs.Empty);
        };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!IsEditable || Selected is null) return;
        if (e.Key is Key.Delete or Key.Back)
        {
            RemoveSelected();
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 100 : Snap;
            Selected.Y = Clamp(Selected.Y + (e.Key == Key.Up ? -step : step), 0, H - Selected.Height);
            LayoutEdited?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    // ---------- drop target ----------

    private static WidgetKind? KindFrom(DragEventArgs e) =>
        e.Data.GetDataPresent(DragFormat) && Enum.TryParse<WidgetKind>(e.Data.GetData(DragFormat) as string, out var k) ? k : null;

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        var kind = KindFrom(e);
        if (!IsEditable || kind is null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        var info = WidgetCatalog.Get(kind.Value);
        var y = Clamp(SnapTo(e.GetPosition(_canvas).Y - info.DefaultHeight / 2), 0, H - info.DefaultHeight);
        _ghost.Height = info.DefaultHeight;
        _ghostLabel.Text = $"Drop {info.Label}";
        Canvas.SetLeft(_ghost, 20);
        Canvas.SetTop(_ghost, y);
        _ghost.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        _ghost.Visibility = Visibility.Collapsed;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        _ghost.Visibility = Visibility.Collapsed;
        var kind = KindFrom(e);
        if (!IsEditable || kind is null) return;
        Place(kind.Value, e.GetPosition(_canvas).Y);
        Focus();
        e.Handled = true;
    }

    // ---------- helpers ----------

    private static double SnapTo(double v) => Math.Round(v / Snap) * Snap;
    private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

    private bool IsInsideWidget(DependencyObject? d)
    {
        for (; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is WidgetVisual) return true;
        return false;
    }

    private static bool IsInside(DependencyObject? d, DependencyObject target)
    {
        for (; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d == target) return true;
        return false;
    }
}
