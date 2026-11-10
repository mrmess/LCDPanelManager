using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StripView.Controls;
using StripView.Device;
using StripView.Model;

namespace StripView.Views;

/// <summary>Front page: the live studio canvas.</summary>
public partial class HomeView : UserControl
{
    private AppState _state = null!;

    public event Action? RequestDesign;

    public HomeView()
    {
        InitializeComponent();
    }

    public void Attach(AppState state)
    {
        _state = state;
        Strip.Attach(state.Layout, state.Telemetry);
        PauseButton.Click += (_, _) => state.TogglePause();
        PushButton.Click += (_, _) => state.PushOnce();
        ConnectButton.Click += async (_, _) =>
        {
            if (state.Link.SessionOpen) await state.DisconnectAsync();
            else await state.ConnectAsync();
        };
        CadenceButton.Click += (_, _) => state.CycleCadence();
        ViewAllButton.Click += (_, _) => RequestDesign?.Invoke();

        state.TelemetryTick += RefreshDesk;
        state.StreamTick += () => Strip.RefreshValues(state.Telemetry);
        state.StateChanged += RefreshControls;
        state.Layout.PropertyChanged += (_, _) => RefreshScene();
        state.Layout.Widgets.CollectionChanged += (_, _) => RefreshScene();
        RefreshScene();
        RefreshDesk();
    }

    private void RefreshControls()
    {
        PauseLabel.Text = _state.StreamPaused ? "▶   Resume stream" : "❚❚   Pause stream";
        CadenceButton.Content = $"Every {_state.CadenceSeconds} seconds";
        PanelPort.Text = _state.Link.SessionOpen ? _state.Link.PortName : _state.Target?.PortName ?? "NOT DETECTED";
        var link = _state.Link;
        ConnectButton.Content = link.SessionOpen ? "Disconnect" : _state.Connecting ? "Connecting…" : "Connect";
        ConnectButton.IsEnabled = link.SessionOpen ? !link.PushBusy : _state.CanConnect;
        ConnectButton.ToolTip = link.SessionOpen ? "Close the port." : PanelLink.RefusalFor(_state.Target) ?? "Open the case LCD and check it is ready.";
        PushButton.IsEnabled = _state.CanSend;
        SendNote.Text = link.PushBusy ? "Sending…"
            : link.CanPush ? (_state.StreamPaused ? "Connected. Paused: nothing is sent." : $"Connected. Sending every {_state.CadenceSeconds} s.")
            : link.SessionOpen ? "Connected, panel not ready."
            : _state.Target is null ? "Case LCD not detected. Preview only." : "Not connected. Preview only.";
    }

    private void RefreshScene()
    {
        var theme = SceneTheme.Get(_state.Layout.ThemeId);
        SceneTitle.Text = theme.Title;
        SceneBlurb.Text = theme.Blurb;
        WidgetCount.Text = $"{_state.Layout.Widgets.Count} LIVE WIDGETS";

        SceneList.Children.Clear();
        foreach (var t in SceneTheme.All)
        {
            var active = t.Id == theme.Id;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new Border { Width = 30, Height = 42, CornerRadius = new CornerRadius(5), Background = t.Swatch });
            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextPrimary") });
            text.Children.Add(new TextBlock
            {
                Text = active ? $"{t.Kind} · Active" : t.Kind,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondary"),
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            if (active)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = (Brush)FindResource("Lime"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(dot, 2);
                row.Children.Add(dot);
            }
            var b = new Button
            {
                Style = (Style)FindResource("BaseButton"),
                Content = row,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Background = (Brush)FindResource(active ? "SurfaceRaised" : "Surface"),
                BorderBrush = (Brush)FindResource(active ? "LineStrong" : "Line"),
            };
            var id = t.Id;
            b.Click += (_, _) => _state.Layout.ThemeId = id;
            SceneList.Children.Add(b);
        }
    }

    private void RefreshDesk()
    {
        var t = _state.Telemetry;
        SensorCount.Text = $"SENSORS {t.LiveSensorCount} LIVE";

        CpuRing.Value = t.CpuPercent;
        CpuValue.Text = $"{t.CpuPercent:0}%";
        CpuTemp.Text = t.CpuTempC is double cpuTemp ? $"{cpuTemp:0}°" : "n/a";
        CpuTrend.Text = $"{(t.CpuTrend >= 0 ? "↗" : "↘")} {Math.Abs(t.CpuTrend):0}% this minute";

        GpuRing.Value = t.GpuPercent ?? 0;
        GpuRing.Opacity = t.GpuPercent.HasValue ? 1 : 0.35;
        GpuValue.Text = t.GpuPercent is double gpu ? $"{gpu:0}%" : "n/a";
        GpuTemp.Text = t.GpuTempC is double gpuTemp ? $"{gpuTemp:0}°" : "n/a";
        GpuSourceLabel.Visibility = t.GpuTempC.HasValue ? Visibility.Visible : Visibility.Collapsed;

        MemValue.Text = $"{t.MemPercent:0}%";
        MemDetail.Text = $"· {t.MemUsedGb:0.0} / {t.MemTotalGb:0} GB";
        MemTotal.Text = $"{t.MemTotalGb:0} GB";
        MemBar.Percent = t.MemPercent;

        NetTitle.Text = $"NETWORK / {t.NetAdapter.ToUpperInvariant()}";
        DownValue.Text = TelemetrySource.FormatMbps(t.DownMbps);
        UpValue.Text = TelemetrySource.FormatMbps(t.UpMbps);
        NetSpark.SetData(t.DownHistory, t.UpHistory);

        DriveCount.Text = $"{t.Drives.Count} FIXED DRIVES";
        var fills = new[] { "Cyan", "Lime", "Purple", "Orange" };
        DriveList.Children.Clear();
        for (var i = 0; i < t.Drives.Count; i++)
        {
            var d = t.Drives[i];
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.Children.Add(new TextBlock { Text = d.Letter, FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextPrimary") });
            var bar = new Bar { Fill = (Brush)FindResource(fills[i % fills.Length]), Percent = d.UsedPercent, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(new TextBlock { Text = $"{d.UsedPercent:0}%", FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)FindResource("TextPrimary") });
            right.Children.Add(new TextBlock { Text = d.FreeText, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)FindResource("TextSecondary") });
            Grid.SetColumn(right, 2);
            row.Children.Add(right);
            DriveList.Children.Add(row);
        }
    }
}
