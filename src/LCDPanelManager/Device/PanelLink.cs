using System.IO;

namespace LCDPanelManager.Device;

public readonly record struct PushResult(bool Sent, string Message);

public enum LinkState
{
    Disconnected,
    Connecting,
    Ready,
    Pushing,
    NotReady,
}

/// <summary>
/// LCD Panel Manager's session around <see cref="SerialPanelClient"/>. The Connect, Push and Disconnect
/// sequences follow Connect, then an optional still or live push, then Disconnect.
/// The identity lock is checked again
/// right before the port is opened; a refused or unknown port is never opened.
/// All methods are called from the UI thread; serial work runs on Task.Run.
/// </summary>
public sealed class PanelLink : IDisposable
{
    /// <summary>Default brightness byte for the 0x7B/0x7D prelude frames. Never 0xA0 implicitly.</summary>
    public const byte DefaultBrightness = 0x79;
    public const byte MinBrightness = 0x20;
    public const byte MaxBrightness = 0xA0;
    public const int BaudRate = 115200;
    public const int KeepAliveMs = 800;

    private readonly SerialPanelClient _client = new();
    private CancellationTokenSource? _pushCts;
    private bool _txHealthy;
    private bool _pushBusy;

    public PanelLink()
    {
        _client.OnTxLockedOut = () =>
        {
            _txHealthy = false;
            TxLockedOutRaised?.Invoke();
        };
    }

    /// <summary>Raised from a background thread when keep-alive or a write hits Device Error.</summary>
    public event Action? TxLockedOutRaised;

    public bool WritesAuthorized => true;
    public bool IsOpen => _client.IsOpen;
    public bool SessionOpen => _client.SessionOpen;
    public bool TxLockedOut => _client.TxLockedOut;
    public bool TxHealthy => _txHealthy;
    public bool PushBusy => _pushBusy;
    public string? PortName { get; private set; }
    public LinkState State { get; private set; } = LinkState.Disconnected;
    public DateTime? LastSent { get; private set; }
    public string? LastError { get; private set; }
    public bool CanPush => _txHealthy && _client.SessionOpen && !_pushBusy && !_client.TxLockedOut;

    /// <summary>Why this port may not be opened, or null if it is the locked target.</summary>
    public static string? RefusalFor(PortInfo? target)
    {
        if (target is null)
            return $"No {DeviceIdentity.StripName} found ({DeviceIdentity.StripId}).";
        var verdict = PortScanner.Classify(target.PortName, target.Vid, target.Pid, target.FriendlyName);
        return verdict.Verdict == PortVerdict.Target ? null : $"{target.PortName} refused: {verdict.Reason}";
    }

    public async Task<string?> ConnectAsync(PortInfo? target)
    {
        if (_client.SessionOpen) return "Already connected.";

        // Re-check identity live, right before opening. Stop on any mismatch.
        var refusal = RefusalFor(target);
        if (refusal is not null) return Fail(refusal);
        var fresh = PortScanner.Scan().FirstOrDefault(p => p.PortName.Equals(target!.PortName, StringComparison.OrdinalIgnoreCase));
        refusal = RefusalFor(fresh);
        if (refusal is not null) return Fail("Identity check before open failed. " + refusal);
        if (fresh!.Vid != target!.Vid || fresh.Pid != target.Pid) return Fail($"{target.PortName} changed identity since the last scan. Not opened.");

        var port = fresh.PortName;
        _txHealthy = false;
        State = LinkState.Connecting;
        try
        {
            // Host COM open is not the same as panel ready - CDC can look open while MCU is hung.
            await Task.Run(() => _client.Open(port, BaudRate, softReset: false));
            if (!_client.IsOpen)
                throw new IOException("Port opened then immediately reported closed.");
            PortName = port;

            // Honest readiness: CF write + drain-wait + 700 ms settle + handle must stay open.
            var health = await Task.Run(() => _client.ProbeTxHealth(CancellationToken.None));
            if (health is null && _client.IsOpen && !_client.TxLockedOut)
            {
                _txHealthy = true;
                _client.StartKeepAlive(KeepAliveMs);
                State = LinkState.Ready;
                LastError = null;
                return null;
            }
            try { _client.Close(); } catch { /* ignore */ }
            State = LinkState.Disconnected;
            return Fail(health ?? SerialPanelClient.PanelNotRespondingMessage);
        }
        catch (Exception ex)
        {
            _txHealthy = false;
            try { _client.Close(); } catch { /* ignore */ }
            State = LinkState.Disconnected;
            return Fail("Connect failed: " + FormatSerialError(ex));
        }
    }

    /// <summary>One full 440x1920 frame: stock still prelude + C8 stream, as the earlier app's PushStill.</summary>
    public async Task<PushResult> PushFrameAsync(byte[] bgra, byte brightness, Action<long, long>? progress = null)
    {
        if (_pushBusy) return new(false, "Push already in progress.");
        if (!_client.SessionOpen) return new(false, "Connect first.");
        if (!_txHealthy) return new(false, "Panel not ready (TX health failed). Disconnect, then Connect again.");
        if (bgra.Length != FrameFormat.FullFrameBytes) return new(false, $"Frame must be {FrameFormat.FullFrameBytes:N0} BGRA bytes.");
        brightness = Math.Clamp(brightness, MinBrightness, MaxBrightness);

        _pushBusy = true;
        State = LinkState.Pushing;
        _client.StopKeepAlive();
        try
        {
            if (_client.TxLockedOut)
            {
                _txHealthy = false;
                throw new IOException(SerialPanelClient.PanelNotAcceptingMessage);
            }
            _pushCts?.Dispose();
            _pushCts = new CancellationTokenSource();
            var token = _pushCts.Token;
            await Task.Run(() => _client.PushStillExperimental(
                bgra,
                brightnessByte: brightness,
                onePage: true,
                progress: progress,
                cancellationToken: token), token);
            if (token.IsCancellationRequested) return new(false, "Push cancelled.");
            _client.StartKeepAlive(KeepAliveMs);
            LastSent = DateTime.Now;
            State = LinkState.Ready;
            LastError = null;
            return new(true, $"Sent to {PortName} at {LastSent:HH:mm:ss} (brightness 0x{brightness:X2}).");
        }
        catch (OperationCanceledException)
        {
            return new(false, "Push cancelled.");
        }
        catch (Exception ex)
        {
            var msg = FormatSerialError(ex);
            var hardFail = _client.TxLockedOut || SerialPanelClient.IsDeviceError(ex) ||
                           msg.Contains("locked out", StringComparison.OrdinalIgnoreCase);
            _txHealthy = false;
            if (hardFail)
            {
                try { _client.RecoverAfterWriteFailure(); } catch { /* ignore */ }
            }
            LastError = "Push failed: " + msg;
            return new(false, LastError);
        }
        finally
        {
            _pushBusy = false;
            if (State == LinkState.Pushing)
                State = _client.SessionOpen ? (_txHealthy ? LinkState.Ready : LinkState.NotReady) : LinkState.Disconnected;
            try { _pushCts?.Dispose(); } catch { /* ignore */ }
            _pushCts = null;
        }
    }

    /// <summary>Cancel any push, flush the panel back to its onboard default image, then close the port (2 s ceiling).</summary>
    public async Task DisconnectAsync()
    {
        try { _pushCts?.Cancel(); } catch { /* ignore */ }
        var closeTask = Task.Run(() =>
        {
            try { _client.Disconnect(); }
            catch { try { _client.Close(); } catch { /* ignore */ } }
        });
        var finished = await Task.WhenAny(closeTask, Task.Delay(2000));
        if (finished != closeTask)
        {
            try { _client.Close(); } catch { /* ignore */ }
        }
        else
        {
            try { await closeTask; } catch { /* ignore */ }
        }
        _pushBusy = false;
        _txHealthy = false;
        State = LinkState.Disconnected;
    }

    /// <summary>Port of SyncConnectionStatus: recover the known deferred IsOpen flap, else demote.</summary>
    public void Sync()
    {
        if (_pushBusy) return;
        if (_client.SessionOpen && !_client.IsOpen)
        {
            var wasHealthy = _txHealthy;
            var locked = _client.TxLockedOut;
            try { _client.EnsureLiveOpen(softResetOnReopen: false); } catch { /* ignore */ }
            if (!locked && _client.IsOpen && wasHealthy)
            {
                _txHealthy = true;
                try { _client.StartKeepAlive(KeepAliveMs); } catch { /* ignore */ }
                State = LinkState.Ready;
                return;
            }
            if (!locked && _client.IsOpen && !wasHealthy)
            {
                State = LinkState.NotReady;
                return;
            }
            _txHealthy = false;
            try { _client.StopKeepAlive(); } catch { /* ignore */ }
            State = LinkState.Disconnected;
            LastError = wasHealthy || locked
                ? "Connection lost after TX (handle dropped). Connect again, then Push."
                : "Connection lost (adapter closed handle). Connect again.";
        }
        else if (_client.IsOpen)
        {
            if (!_txHealthy || _client.TxLockedOut)
            {
                _txHealthy = _txHealthy && !_client.TxLockedOut;
                State = LinkState.NotReady;
            }
        }
        else if (State != LinkState.Connecting)
        {
            State = LinkState.Disconnected;
        }
    }

    /// <summary>App exit: flush the panel back to its onboard default image, then close the port.</summary>
    public void Dispose()
    {
        try { _pushCts?.Cancel(); } catch { /* ignore */ }
        try { _client.Disconnect(); } catch { _client.Dispose(); }
    }

    private string Fail(string message)
    {
        LastError = message;
        return message;
    }

    private static string FormatSerialError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is UnauthorizedAccessException)
                return "Port is busy (another app has it open). Close the other app, then Connect again.";
        }
        return ex.Message;
    }
}
