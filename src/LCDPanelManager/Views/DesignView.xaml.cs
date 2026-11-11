using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using LCDPanelManager.Controls;
using LCDPanelManager.Device;
using LCDPanelManager.Model;

namespace LCDPanelManager.Views;

/// <summary>The editor: drag widgets from the palette onto the strip.</summary>
public partial class DesignView : UserControl
{
    private AppState _state = null!;
    private readonly Dictionary<WidgetKind, TextBlock> _chipValues = new();
    private Point _dragStart;
    private WidgetKind? _pressedKind;
    private bool _syncing;

    public DesignView()
    {
        InitializeComponent();
    }

    public void Attach(AppState state)
    {
        _state = state;
        Strip.Attach(state.Layout, state.Telemetry);
        WidgetList.ItemsSource = state.Layout.Widgets;

        BuildPalette();
        BuildThemeTiles();
        state.Layout.PropertyChanged += (_, _) => BuildThemeTiles();

        Strip.SelectionChanged += (_, _) => SyncSelection();
        Strip.LayoutEdited += (_, _) => state.SaveLayout();
        WidgetList.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            Strip.Select(WidgetList.SelectedItem as WidgetItem);
        };

        HeightSlider.ValueChanged += (_, e) =>
        {
            if (_syncing || Strip.Selected is not { } w) return;
            w.Height = Math.Min(e.NewValue, StripLayout.StripHeight);
            w.Y = Math.Min(w.Y, StripLayout.StripHeight - w.Height);
            HeightLabel.Text = $"{w.Height:0} px";
        };
        ItemText.TextChanged += (_, _) =>
        {
            if (_syncing || Strip.Selected is not { } w) return;
            w.Text = ItemText.Text;
        };
        ChooseImageButton.Click += (_, _) => ChooseImage();
        RemoveButton.Click += (_, _) => Strip.RemoveSelected();
        ResetButton.Click += (_, _) =>
        {
            if (MessageBox.Show("Replace the current layout with the Aurora default?", "Reset to default layout",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Strip.Select(null);
            state.Layout.ResetToDefault();
            state.SaveLayout();
            state.Log("Layout reset to the default scene.");
        };
        SaveButton.Click += (_, _) =>
        {
            state.SaveLayout();
            state.SetStatus($"Layout saved to {StripLayout.StorePath}");
        };

        BrightnessSlider.Value = state.Brightness;
        UpdateBrightnessLabel();
        BrightnessSlider.ValueChanged += (_, e) =>
        {
            state.Brightness = (byte)Math.Round(e.NewValue);
            UpdateBrightnessLabel();
        };
        ApplyBrightnessButton.Click += (_, _) => state.ApplyBrightness();
        ConnectButton.Click += async (_, _) =>
        {
            if (state.Link.SessionOpen) await state.DisconnectAsync();
            else await state.ConnectAsync();
        };
        SendButton.Click += (_, _) => state.PushOnce();
        LiveToggle.Checked += (_, _) => { if (state.StreamPaused) state.TogglePause(); };
        LiveToggle.Unchecked += (_, _) => { if (!state.StreamPaused) state.TogglePause(); };
        CadenceButton.Click += (_, _) => state.CycleCadence();

        state.TelemetryTick += RefreshChips;
        state.StreamTick += () => Strip.RefreshValues(state.Telemetry);
        state.StateChanged += RefreshDevice;
        SyncSelection();
        RefreshDevice();
        RefreshChips();
    }

    // ---------- palette (drag source) ----------

    private void BuildPalette()
    {
        Palette.Children.Clear();
        foreach (var info in WidgetCatalog.All)
        {
            var name = new TextBlock { Text = info.Label, Foreground = (Brush)FindResource("TextSecondary"), VerticalAlignment = VerticalAlignment.Center };
            var value = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextPrimary"), HorizontalAlignment = HorizontalAlignment.Right };
            _chipValues[info.Kind] = value;
            var g = new Grid();
            g.Children.Add(name);
            g.Children.Add(value);
            var chip = new Border
            {
                Style = (Style)FindResource("SensorChip"),
                Width = 170,
                Child = g,
                Cursor = Cursors.Hand,
                ToolTip = $"Drag {info.Label} onto the strip" + (info.IsSample ? " (sample data for now)" : ""),
            };
            var kind = info.Kind;
            chip.MouseLeftButtonDown += (_, e) =>
            {
                _pressedKind = kind;
                _dragStart = e.GetPosition(this);
                if (e.ClickCount == 2)
                {
                    _pressedKind = null;
                    Strip.Append(kind);
                }
            };
            chip.MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _pressedKind != kind) return;
                var d = e.GetPosition(this) - _dragStart;
                if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                _pressedKind = null;
                chip.Opacity = 0.5;
                DragDrop.DoDragDrop(chip, new DataObject(StripCanvas.DragFormat, kind.ToString()), DragDropEffects.Copy);
                chip.Opacity = 1;
            };
            chip.MouseLeftButtonUp += (_, _) => _pressedKind = null;
            chip.MouseEnter += (_, _) => chip.BorderBrush = (Brush)FindResource("Cyan");
            chip.MouseLeave += (_, _) => chip.BorderBrush = (Brush)FindResource("Line");
            Palette.Children.Add(chip);
        }
    }

    private void RefreshChips()
    {
        var t = _state.Telemetry;
        var c = t.Drive("C:");
        foreach (var (kind, tb) in _chipValues)
        {
            tb.Text = kind switch
            {
                WidgetKind.Cpu => $"{t.CpuPercent:0}%",
                WidgetKind.Gpu => t.GpuPercent is double gpu ? $"{gpu:0}%" : "n/a",
                WidgetKind.GpuTemp => TelemetrySource.FormatTemperature(t.GpuTempC),
                WidgetKind.Memory => $"{t.MemPercent:0}%",
                WidgetKind.Drive => c is null ? "—" : TelemetrySource.FormatGb(c.FreeGb),
                WidgetKind.Network => $"{TelemetrySource.FormatMbps(t.DownMbps)} Mb/s",
                WidgetKind.Clock => DateTime.Now.ToString("HH:mm"),
                WidgetKind.Text => "Aa",
                WidgetKind.Image => "▣",
                _ => "",
            };
        }
    }

    // ---------- themes ----------

    private void BuildThemeTiles()
    {
        ThemeTiles.Children.Clear();
        foreach (var t in SceneTheme.All)
        {
            var selected = t.Id == _state.Layout.ThemeId;
            var light = t.Id == "monochrome";
            var inner = new Grid();
            var mock = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -14, 0, 0) };
            mock.Children.Add(new Ellipse { Width = 30, Height = 30, Stroke = light ? Brushes.DimGray : t.Accent1, StrokeThickness = 2.5, HorizontalAlignment = HorizontalAlignment.Center });
            mock.Children.Add(new Border { Width = 46, Height = 4, CornerRadius = new CornerRadius(2), Background = light ? Brushes.Silver : t.Accent2, Margin = new Thickness(0, 14, 0, 0) });
            mock.Children.Add(new Border { Width = 32, Height = 4, CornerRadius = new CornerRadius(2), Background = light ? Brushes.LightGray : t.Accent3, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0) });
            inner.Children.Add(mock);
            inner.Children.Add(new TextBlock
            {
                Text = t.Name,
                FontSize = 11,
                Foreground = light ? Brushes.Black : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 8),
            });
            var tile = new Border
            {
                Width = 74,
                Height = 128,
                CornerRadius = new CornerRadius(6),
                Background = t.Swatch,
                BorderBrush = selected ? (Brush)FindResource("Cyan") : (Brush)FindResource("LineStrong"),
                BorderThickness = new Thickness(selected ? 2 : 1),
                Margin = new Thickness(0, 0, 14, 0),
                Child = inner,
                Cursor = Cursors.Hand,
                ToolTip = t.Blurb,
            };
            var id = t.Id;
            tile.MouseLeftButtonUp += (_, _) => _state.Layout.ThemeId = id;
            ThemeTiles.Children.Add(tile);
        }
        var import = new Border
        {
            Width = 74,
            Height = 128,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x12)),
            BorderBrush = (Brush)FindResource("LineStrong"),
            BorderThickness = new Thickness(1),
            Opacity = 0.6,
            ToolTip = "Theme import comes later.",
            Child = new TextBlock { Text = "+\nImport", TextAlignment = TextAlignment.Center, Foreground = (Brush)FindResource("TextSecondary"), VerticalAlignment = VerticalAlignment.Center },
        };
        ThemeTiles.Children.Add(import);
    }

    // ---------- selection / inspector ----------

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            var w = Strip.Selected;
            WidgetList.SelectedItem = w;
            Inspector.IsEnabled = w is not null;
            Inspector.Opacity = w is null ? 0.45 : 1;
            if (w is null)
            {
                SelectedTitle.Text = "Nothing selected";
                HeightLabel.Text = "";
                ItemText.Text = "";
                TextRow.Visibility = Visibility.Visible;
                ChooseImageButton.Visibility = Visibility.Collapsed;
                return;
            }
            var info = WidgetCatalog.Get(w.Kind);
            SelectedTitle.Text = $"Selected: {info.Label}" + (info.IsSample ? " (sample data)" : "");
            HeightSlider.Value = w.Height;
            HeightLabel.Text = $"{w.Height:0} px";
            TextRow.Visibility = w.Kind is WidgetKind.Text or WidgetKind.Drive ? Visibility.Visible : Visibility.Collapsed;
            TextLabel.Text = w.Kind == WidgetKind.Drive ? "Drive letter (for example C:)" : "Text";
            ItemText.Text = w.Text;
            ChooseImageButton.Visibility = w.Kind == WidgetKind.Image ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ChooseImage()
    {
        if (Strip.Selected is not { Kind: WidgetKind.Image } w) return;
        var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" };
        if (dlg.ShowDialog() == true)
        {
            w.ImagePath = dlg.FileName;
            _state.SaveLayout();
        }
    }

    private void UpdateBrightnessLabel()
    {
        var b = _state.Brightness;
        var word = b == PanelLink.DefaultBrightness ? "Standard" : b < PanelLink.DefaultBrightness ? "Dimmer" : "Brighter";
        BrightnessLabel.Text = $"{word} · 0x{b:X2}";
    }

    // ---------- device card ----------

    private void RefreshDevice()
    {
        var t = _state.Target;
        var link = _state.Link;
        DeviceLine.Text = t is null
            ? $"USB CDC · {DeviceIdentity.StripId.Replace("&", " & ")} · not detected"
            : $"USB CDC · VID_{t.Vid} & PID_{t.Pid} · {t.PortName}";
        DeviceState.Text = link.PushBusy ? "Sending" : link.CanPush ? "Live" : link.SessionOpen ? "Connected · not ready"
            : _state.Connecting ? "Connecting" : t is null ? "Not detected" : "Detected · not connected";
        var brush = (Brush)FindResource(link.TxHealthy ? "Ok" : t is null || link.TxLockedOut ? "Bad" : "Warn");
        ConnectButton.Content = link.SessionOpen ? "Disconnect" : _state.Connecting ? "Connecting…" : "Connect";
        ConnectButton.IsEnabled = link.SessionOpen ? !link.PushBusy : _state.CanConnect;
        ConnectButton.ToolTip = link.SessionOpen ? "Close the port." : PanelLink.RefusalFor(t) ?? "Open the case LCD and check it is ready.";
        SendButton.IsEnabled = _state.CanSend;
        ApplyBrightnessButton.IsEnabled = _state.CanSend;
        if (link.LastSent is { } sent)
        {
            LastSentChip.Opacity = 1;
            LastSentChip.ToolTip = $"Last frame sent at {sent:HH:mm:ss}";
            LastSentText.Text = $"Last sent {sent:HH:mm:ss}";
        }
        DeviceDot.Fill = brush;
        DeviceState.Foreground = brush;
        DevicePill.BorderBrush = (Brush)FindResource("LineStrong");
        LiveToggle.IsChecked = !_state.StreamPaused;
        CadenceButton.Content = $"every {_state.CadenceSeconds} s";
    }
}
