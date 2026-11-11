using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LCDPanelManager.Controls;
using LCDPanelManager.Device;
using LCDPanelManager.Model;

namespace LCDPanelManager;

/// <summary>Shared state for Home, Design and Diagnostics. One 1-second clock drives everything.</summary>
public sealed class AppState : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _seconds;
    private DateTime _lastLivePush = DateTime.MinValue;
    private DateTime _autoPushNotBefore = DateTime.MinValue;

    private readonly TelemetrySource _telemetrySource = new();
    private bool _telemetryBusy;
    private string? _telemetryError;
    public TelemetrySnapshot Telemetry { get; private set; } = TelemetrySnapshot.Empty;
    public StripLayout Layout { get; } = StripLayout.LoadOrDefault();
    public PanelLink Link { get; } = new();
    public IReadOnlyList<PortInfo> Ports { get; private set; } = Array.Empty<PortInfo>();
    public PortInfo? Target { get; private set; }
    public bool StreamPaused { get; private set; }
    public int CadenceSeconds { get; private set; } = 2;
    public byte Brightness { get; set; } = PanelLink.DefaultBrightness;
    public DateTime? LastRendered { get; private set; }
    public string Status { get; private set; } = "Starting…";
    public bool Connecting { get; private set; }
    public ObservableCollection<string> LogLines { get; } = new();

    /// <summary>Every second: fresh host telemetry.</summary>
    public event Action? TelemetryTick;
    /// <summary>Every cadence tick while the stream runs, and on Push once: re-render the strip.</summary>
    public event Action? StreamTick;
    /// <summary>Devices, link, pause, cadence or status changed.</summary>
    public event Action? StateChanged;

    public AppState()
    {
        _timer.Tick += (_, _) => Tick();
        Link.TxLockedOutRaised += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Log("TX locked out (Device Error). Disconnect, then Connect again.");
            SetStatus("TX locked out. Disconnect, then Connect again. Live sending stopped.");
        });
    }

    /// <summary>The Connect button is enabled only for a present, unrefused target and no open session.</summary>
    public bool CanConnect => !Connecting && !Link.SessionOpen && PanelLink.RefusalFor(Target) is null;
    public bool CanSend => Link.CanPush;

    public Task ToggleConnectAsync() => Link.SessionOpen ? DisconnectAsync() : ConnectAsync();

    public void ApplyConnectButton(Button button)
    {
        var link = Link;
        button.Content = link.SessionOpen ? "Disconnect" : Connecting ? "Connecting…" : "Connect";
        button.IsEnabled = link.SessionOpen ? !link.PushBusy : CanConnect;
        button.ToolTip = link.SessionOpen
            ? "Close the port."
            : PanelLink.RefusalFor(Target) ?? "Open the case LCD and check it is ready.";
    }

    public void Start()
    {
        Rescan();
        _timer.Start();
        Render();
        _ = RefreshTelemetryAsync(render: true);
        SetStatus(Target is null
            ? $"{DeviceIdentity.StripName} not detected. Preview only."
            : $"{DeviceIdentity.StripName} found on {Target.PortName}. Click Connect to send to the panel.");
    }

    private async void Tick()
    {
        await RefreshTelemetryAsync();
        var before = Link.State;
        Link.Sync();
        if (Link.State != before && Link.LastError is not null && Link.State == LinkState.Disconnected)
        {
            Log(Link.LastError);
            SetStatus(Link.LastError);
        }
        _seconds++;
        if (!StreamPaused && _seconds % CadenceSeconds == 0) Render();

        var now = DateTime.UtcNow;
        if (!StreamPaused && Link.CanPush && now >= _autoPushNotBefore &&
            now - _lastLivePush >= TimeSpan.FromSeconds(CadenceSeconds))
        {
            _lastLivePush = now;
            _ = SendFrameAsync(live: true);
        }
        StateChanged?.Invoke();
    }

    private async Task RefreshTelemetryAsync(bool render = false)
    {
        if (_telemetryBusy) return;
        _telemetryBusy = true;
        try
        {
            Telemetry = await Task.Run(() => _telemetrySource.Sample());
            _telemetryError = null;
            TelemetryTick?.Invoke();
            if (render) Render();
        }
        catch (Exception ex)
        {
            if (_telemetryError != ex.Message)
            {
                _telemetryError = ex.Message;
                Log("Telemetry sample failed: " + ex.Message);
            }
        }
        finally
        {
            _telemetryBusy = false;
        }
    }

    private void Render()
    {
        LastRendered = DateTime.Now;
        StreamTick?.Invoke();
        StateChanged?.Invoke();
    }

    public void TogglePause()
    {
        StreamPaused = !StreamPaused;
        Log(StreamPaused ? "Stream paused. No live frames are sent." : "Stream resumed.");
        SetStatus(StreamPaused
            ? "Stream paused. The preview is frozen and nothing is sent."
            : Link.CanPush ? $"Stream running. Sending every {CadenceSeconds} s." : $"Stream running. Preview refreshes every {CadenceSeconds} s.");
    }

    public void CycleCadence()
    {
        CadenceSeconds = CadenceSeconds switch { 2 => 3, 3 => 5, _ => 2 };
        Log($"Refresh cadence set to {CadenceSeconds} s.");
        StateChanged?.Invoke();
    }

    public async Task ConnectAsync()
    {
        if (!CanConnect)
        {
            var why = PanelLink.RefusalFor(Target) ?? (Link.SessionOpen ? "Already connected." : "Busy.");
            Log("Connect refused: " + why);
            SetStatus(why);
            return;
        }
        Connecting = true;
        SetStatus($"Opening {Target!.PortName} and checking the panel is ready…");
        Log($"Connect: opening {Target.PortName} ({Target.UsbId}) at {PanelLink.BaudRate} 8N1, then CF probe.");
        try
        {
            var err = await Link.ConnectAsync(Target);
            if (err is null)
            {
                Log($"Connected: {Link.PortName} ready. Keep-alive every {PanelLink.KeepAliveMs} ms.");
                SetStatus($"Connected · {DeviceIdentity.StripName} on {Link.PortName} · ready.");
            }
            else
            {
                Log("Connect: " + err);
                SetStatus(err);
            }
        }
        finally
        {
            Connecting = false;
            StateChanged?.Invoke();
        }
    }

    public async Task DisconnectAsync()
    {
        SetStatus("Disconnecting…");
        await Link.DisconnectAsync();
        Log("Disconnected. Panel released to its default image, port closed.");
        SetStatus("Disconnected. Panel returned to its default image.");
    }

    public void PushOnce() => _ = SendFrameAsync(live: false);

    /// <summary>Re-sends the current frame so the new brightness byte reaches the panel.</summary>
    public void ApplyBrightness()
    {
        Log($"Brightness 0x{Brightness:X2}: applied with the next frame.");
        _ = SendFrameAsync(live: false);
    }

    private async Task SendFrameAsync(bool live)
    {
        Render();
        var frame = StripRenderer.RenderBgra(Layout, Telemetry);
        if (!Link.CanPush)
        {
            var why = Link.SessionOpen ? (Link.PushBusy ? "A send is already in progress." : "Panel not ready.") : "Not connected.";
            if (!live)
            {
                Log($"Push once: rendered {frame.Length:N0}-byte frame, not sent. {why}");
                SetStatus($"Frame rendered, not sent: {why}");
            }
            return;
        }
        if (!live) SetStatus($"Sending one frame to {Link.PortName}…");
        StateChanged?.Invoke();
        var r = await Link.PushFrameAsync(frame, Brightness);
        if (!r.Sent)
        {
            _autoPushNotBefore = DateTime.UtcNow.AddSeconds(10);
            Log((live ? "Live frame: " : "Push once: ") + r.Message);
            SetStatus(r.Message);
        }
        else if (!live)
        {
            Log("Push once: " + r.Message);
            SetStatus(r.Message);
        }
        else
        {
            SetStatus($"Live · last sent {Link.LastSent:HH:mm:ss} · {DeviceIdentity.StripName} on {Link.PortName}");
        }
    }

    public void Rescan()
    {
        Ports = PortScanner.Scan();
        Target = PortScanner.SelectTarget(Ports);
        Log(Ports.Count == 0 ? "Port scan: no COM ports present." : $"Port scan: {Ports.Count} COM port(s).");
        foreach (var p in Ports) Log($"  {p.PortName}  {p.UsbId}  {p.Verdict}: {p.Reason}");
        Log(Target is null ? $"No {DeviceIdentity.StripName} ({DeviceIdentity.StripId}) present." : $"Target: {Target.PortName}.");
        StateChanged?.Invoke();
    }

    public void SetStatus(string text)
    {
        Status = text;
        StateChanged?.Invoke();
    }

    public void Log(string message)
    {
        LogLines.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (LogLines.Count > 400) LogLines.RemoveAt(LogLines.Count - 1);
    }

    public void SaveLayout()
    {
        try
        {
            Layout.Save();
        }
        catch (Exception ex)
        {
            Log("Could not save layout: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _telemetrySource.Dispose();
        Link.Dispose();
    }
}
