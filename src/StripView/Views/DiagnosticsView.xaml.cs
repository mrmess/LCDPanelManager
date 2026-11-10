using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StripView.Device;

namespace StripView.Views;

/// <summary>Connection and protocol status. Read-only: it never opens or writes a port.</summary>
public partial class DiagnosticsView : UserControl
{
    private AppState _state = null!;

    public DiagnosticsView()
    {
        InitializeComponent();
    }

    public void Attach(AppState state)
    {
        _state = state;
        LogList.ItemsSource = state.LogLines;
        RescanButton.Click += (_, _) => state.Rescan();
        SelfTestButton.Click += (_, _) => SelfTest();
        state.StateChanged += Refresh;

        LockRules.Text =
            $"Send target: {DeviceIdentity.StripName}, USB {DeviceIdentity.StripId}, normally {DeviceIdentity.ExpectedPort}. " +
            "Matched by USB ID first; COM3 is preferred when several match.\n" +
            $"Refused: {DeviceIdentity.RefusedName}, USB {DeviceIdentity.RefusedId}, and anything on {DeviceIdentity.RefusedPort}, whatever it reports.\n" +
            "Any other port is listed for information and never offered.";

        ProtocolText.Text =
            "Control frame  [cmd][EF][69][u32 big-endian value][trailer][payload], zero-padded to 250 bytes\n" +
            $"Opcodes        C8 full frame · CC partial update · CF keep-alive (value 1, about 1.3 s) · CA close\n" +
            $"Full frame     {FrameFormat.Width}×{FrameFormat.Height} BGRA = {FrameFormat.FullFrameBytes:N0} bytes\n" +
            $"On the wire    250-byte header + {FrameFormat.FullFrameBuffers:N0} × 250-byte buffers (249 pixel bytes + 1 pad) = {FrameFormat.FullFrameWireBytes:N0} bytes";
        Refresh();
    }

    private void Refresh()
    {
        PortRows.Children.Clear();
        if (_state.Ports.Count == 0)
            PortRows.Children.Add(new TextBlock { Text = "No COM ports present.", Foreground = Res("TextSecondary") });
        foreach (var p in _state.Ports)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            Add(row, 0, new TextBlock { Text = p.PortName, FontWeight = FontWeights.SemiBold, Foreground = Res("TextPrimary") });
            Add(row, 1, new TextBlock { Text = p.UsbId, Foreground = Res("TextSecondary"), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11 });
            var (label, brush) = p.Verdict switch
            {
                PortVerdict.Target => ("Target", Res("Ok")),
                PortVerdict.Refused => ("Refused", Res("Bad")),
                _ => ("Not offered", Res("TextSecondary")),
            };
            Add(row, 2, new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Foreground = brush });
            var why = p.Reason + (string.IsNullOrEmpty(p.FriendlyName) ? "" : $"  ({p.FriendlyName})");
            Add(row, 3, new TextBlock { Text = why, Foreground = Res("TextSecondary"), TextWrapping = TextWrapping.Wrap });
            PortRows.Children.Add(row);
        }

        var t = _state.Target;
        var rows = new (string, string)[]
        {
            ("Target", t is null ? "Not detected" : $"{t.PortName} · VID_{t.Vid}&PID_{t.Pid}"),
            ("Link", _state.Link.State.ToString()),
            ("Port opened", _state.Link.IsOpen ? $"Yes · {_state.Link.PortName} · {PanelLink.BaudRate} 8N1" : "No"),
            ("COM writes", _state.Link.WritesAuthorized ? "Authorized (locked target only)" : "Off"),
            ("TX health", _state.Link.TxLockedOut ? "Locked out (Device Error)" : _state.Link.TxHealthy ? "Ready (CF probe passed)" : "Not probed"),
            ("Keep-alive", _state.Link.CanPush ? $"CF every {PanelLink.KeepAliveMs} ms" : "Off"),
            ("Stream", _state.StreamPaused ? "Paused (nothing sent)" : $"Every {_state.CadenceSeconds} s"),
            ("Last sent", _state.Link.LastSent?.ToString("HH:mm:ss") ?? "—"),
            ("Brightness", $"0x{_state.Brightness:X2} (default 0x{PanelLink.DefaultBrightness:X2})"),
            ("Last error", _state.Link.LastError ?? "—"),
        };
        LinkGrid.Children.Clear();
        LinkGrid.RowDefinitions.Clear();
        LinkGrid.ColumnDefinitions.Clear();
        LinkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        LinkGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < rows.Length; i++)
        {
            LinkGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = new TextBlock { Text = rows[i].Item1, Foreground = Res("TextSecondary"), Margin = new Thickness(0, 3, 0, 3) };
            var v = new TextBlock { Text = rows[i].Item2, Foreground = Res("TextPrimary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(k, i);
            Grid.SetRow(v, i);
            Grid.SetColumn(v, 1);
            LinkGrid.Children.Add(k);
            LinkGrid.Children.Add(v);
        }
    }

    private void SelfTest()
    {
        static string Hex(byte[] b, int n) => string.Join(" ", b.Take(n).Select(x => x.ToString("X2")));
        var ka = FrameFormat.BuildKeepAlive();
        var ff = FrameFormat.BuildFullFrameHeader();
        SelfTestOut.Text =
            $"Keep-alive  {ka.Length} bytes: {Hex(ka, 8)} …\n" +
            $"Full frame  header {ff.Length} bytes: {Hex(ff, 8)} …\n" +
            "Built in memory only. Nothing was written.";
        _state.Log("Built test frames in memory. Nothing sent.");
    }

    private static void Add(Grid g, int col, UIElement e)
    {
        Grid.SetColumn(e, col);
        g.Children.Add(e);
    }

    private Brush Res(string key) => (Brush)FindResource(key);
}
