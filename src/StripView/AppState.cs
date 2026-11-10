using System.Collections.ObjectModel;
using System.Windows.Threading;
using StripView.Device;
using StripView.Model;

namespace StripView;

/// <summary>Shared state for Home, Design and Diagnostics. One 1-second clock drives everything.</summary>
public sealed class AppState
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _seconds;

    public TelemetrySource Telemetry { get; } = new();
    public StripLayout Layout { get; } = StripLayout.LoadOrDefault();
    public PanelLink Link { get; } = new();
    public IReadOnlyList<PortInfo> Ports { get; private set; } = Array.Empty<PortInfo>();
    public PortInfo? Target { get; private set; }
    public bool StreamPaused { get; private set; }
    public int CadenceSeconds { get; private set; } = 2;
    public double Brightness { get; set; } = 70;
    public DateTime? LastRendered { get; private set; }
    public string Status { get; private set; } = "Starting…";
    public ObservableCollection<string> LogLines { get; } = new();

    /// <summary>Every second: fresh host telemetry.</summary>
    public event Action? TelemetryTick;
    /// <summary>Every cadence tick while the stream runs, and on Push once: re-render the strip.</summary>
    public event Action? StreamTick;
    /// <summary>Devices, pause, cadence or status changed.</summary>
    public event Action? StateChanged;

    public AppState()
    {
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        Telemetry.Sample();
        Rescan();
        _timer.Start();
        Render();
        SetStatus("Preview only. Sending to the panel is off in this build.");
    }

    private void Tick()
    {
        Telemetry.Sample();
        TelemetryTick?.Invoke();
        _seconds++;
        if (!StreamPaused && _seconds % CadenceSeconds == 0) Render();
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
        Log(StreamPaused ? "Stream paused." : "Stream resumed.");
        SetStatus(StreamPaused ? "Stream paused. The preview is frozen." : "Stream running. Preview refreshes every " + CadenceSeconds + " s.");
    }

    public void CycleCadence()
    {
        CadenceSeconds = CadenceSeconds switch { 2 => 3, 3 => 5, _ => 2 };
        Log($"Refresh cadence set to {CadenceSeconds} s.");
        StateChanged?.Invoke();
    }

    public void PushOnce()
    {
        Render();
        var frame = Controls.StripRenderer.RenderBgra(Layout, Telemetry);
        Log($"Push once: rendered a {frame.Length:N0}-byte 440x1920 BGRA frame in memory.");
        var r = Link.PushOnce(Target);
        Log("Push once: " + r.Message);
        SetStatus(r.Message);
    }

    public void Rescan()
    {
        Ports = PortScanner.Scan();
        Target = PortScanner.SelectTarget(Ports);
        Log(Ports.Count == 0 ? "Port scan: no COM ports present." : $"Port scan: {Ports.Count} COM port(s).");
        foreach (var p in Ports) Log($"  {p.PortName}  {p.UsbId}  {p.Verdict}: {p.Reason}");
        Log(Target is null ? $"No {DeviceIdentity.StripName} ({DeviceIdentity.StripId}) present." : $"Target: {Target.PortName}. No port opened.");
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
}
