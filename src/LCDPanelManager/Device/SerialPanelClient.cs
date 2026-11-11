using RJCP.IO.Ports;
using System.IO;
using System.Linq;
using System.Threading;

namespace LCDPanelManager.Device;

/// <summary>
/// Panel serial client. Owns the COM port through RJCP SerialPortStream 2.4.0.
/// Frame layout and opcodes are those used by this USB case LCD.
///
/// TX model (hard-won, do not regress):
///  - One RJCP handle per session; never Close+Open mid-push.
///  - "Panel ready" is EARNED: Connect probes with a CF frame, waits for the host buffer
///    to drain, settles 700 ms, and requires the handle to stay open. A hung panel MCU
///    accepts queued writes then drops the handle ~500 ms later (usbser URB timeout).
///  - Lock out TX only on a real Write Device Error. Clearing requires Disconnect -> Connect.
///  - Push = prelude frames back-to-back on one handle, then the C8 stream as
///    first-250-bytes + 128 KB chunks.
/// LCD Panel Manager changes: namespace, frame builder name, trace folder, neutral messages,
/// removed the 320x240 stub/solid helpers, and the caller must pass the brightness byte.
/// </summary>
public sealed class SerialPanelClient : IDisposable
{
    // C8#1 first write is 250 bytes (8-byte EF69 header + 242 payload bytes);
    // remaining bulk is sent in 128 KB chunks.
    public const int FirstBulkWriteChunkBytes = 250;
    public const int DefaultWriteChunkBytes = 128 * 1024;

    public const string ComWriteStalledMessage =
        "COM write stalled - Disconnect, wait 2s, Connect, retry (soft DTR reset on Connect; no USB unplug needed)";

    public const string PanelNotAcceptingMessage =
        "Panel TX locked out (Device Error on Write). Do not Push again this session - Disconnect, then Connect. If the vendor app can still update the panel, this is a host/handle failure, not a reason to power-cycle.";

    /// <summary>
    /// Probe failed without a Device Error (bytes never drained). That means the panel MCU
    /// is hung; host-side resets do not recover it. Only a VBUS drop does.
    /// </summary>
    public const string PanelNotRespondingMessage =
        "Port open but panel not responding (TX probe failed). Power-cycle the case LCD or reboot the PC so the panel resets, then Connect again. Do not Push while amber.";

    private static byte[] BuildVideoPathPayload(string path)
    {
        var pathBytes = System.Text.Encoding.ASCII.GetBytes(path);
        var payload = new byte[2 + pathBytes.Length];
        // After the trailer, two zero bytes then the ASCII path (same pad shape as 0x7B/0x7D).
        payload[0] = 0x00;
        payload[1] = 0x00;
        Buffer.BlockCopy(pathBytes, 0, payload, 2, pathBytes.Length);
        return payload;
    }

    private SerialPortStream? _port;
    private readonly object _portGate = new();

    public bool IsOpen
    {
        get { lock (_portGate) return _port?.IsOpen == true; }
    }
    public string? PortName
    {
        get { lock (_portGate) return _port?.PortName; }
    }
    public int BaudRate
    {
        get { lock (_portGate) return _port?.BaudRate ?? 0; }
    }

    private string? _lastPortName;
    private int _lastBaud = 115200;
    /// <summary>True after a successful Open until Close/Recover.</summary>
    private bool _sessionOpen;
    private System.Threading.Timer? _keepAliveTimer;
    private int _keepAliveBusy; // 0/1 reentrancy guard
    private int _handleEpoch;   // bumps on Open/Close; diagnostic only - never gate Push
    /// <summary>Set on first Device Error / TX stall. Blocks further TX until Close+Open.</summary>
    private bool _txLockedOut;
    /// <summary>True while PushStillExperimental owns the wire. Blocks EnsureLiveOpen reopen mid-push.</summary>
    private int _pushInProgress; // 0/1

    // Evidence write-trace: every Write enter/exit under _reflect\write-trace-*.txt
    private string _writePhase = "-";
    private long _writeStreamOffset;
    private string? _writeTracePath;
    private readonly object _writeTraceGate = new();

    /// <summary>Optional UI callback when TX dies mid-keepalive or write.</summary>
    public Action? OnTxLockedOut { get; set; }

    /// <summary>True after Device Error until the next successful Open.</summary>
    public bool TxLockedOut
    {
        get { lock (_portGate) return _txLockedOut; }
    }

    public bool SessionOpen
    {
        get { lock (_portGate) return _sessionOpen; }
    }

    /// <summary>Increments on every Open/Close. Diagnostic only - Push must not refuse intentional EnsureLiveOpen.</summary>
    public int HandleEpoch
    {
        get { lock (_portGate) return _handleEpoch; }
    }

    /// <summary>
    /// Live port for TX. Session must be open. If the handle died, reopen (not mid-push).
    /// Throws if never connected or TX locked out. Never locks out solely because IsOpen flapped.
    /// </summary>
    private SerialPortStream RequireLivePort()
    {
        ThrowIfTxLockedOut();
        EnsureLiveOpen(softResetOnReopen: false);
        lock (_portGate)
            return _port ?? throw new InvalidOperationException("Connect first.");
    }

    public static string[] ListPorts() => SerialPortStream.GetPortNames().OrderBy(x => x).ToArray();

    public void Open(string portName, int baudRate = 115200, bool softReset = false, bool resetTxLockout = true)
    {
        Close();
        // Match SERIAL.md / stock SS: 115200 8N1, DTR+RTS, Handshake.None.
        var port = new SerialPortStream(portName, baudRate, 8, Parity.None, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = 1000,
            WriteTimeout = 8_000
            // WriteBufferSize: RJCP 2.4 default (131072) matches stock app; do not force.
        };
        try
        {
            port.Open();
            if (softReset)
                SoftResetLine(port, baudRate);
            TryDiscardBuffers(port);
        }
        catch (UnauthorizedAccessException ex)
        {
            port.Dispose();
            throw new IOException(
                $"Port {portName} is busy (another app has it open). Close any other app using the case LCD, then Connect again.",
                ex);
        }
        catch (IOException ex) when (IsSemaphoreTimeout(ex))
        {
            try { port.Dispose(); } catch { /* ignore */ }
            throw new IOException(
                $"Opening {portName} hit a serial semaphore timeout. Disconnect, wait a few seconds, Connect again (soft DTR reset).",
                ex);
        }
        catch
        {
            try { port.Dispose(); } catch { /* ignore */ }
            throw;
        }
        lock (_portGate)
        {
            _port = port;
            _sessionOpen = true;
            _lastPortName = portName;
            _lastBaud = baudRate;
            _handleEpoch++;
            if (resetTxLockout)
                _txLockedOut = false;
        }
    }

    /// <summary>
    /// Ensure a live open handle. If session says connected but the handle died after TX,
    /// reopen without soft DTR reset. Throws "Connect first." if never connected this session.
    /// </summary>
    public void EnsureLiveOpen(bool softResetOnReopen = false)
    {
        lock (_portGate)
        {
            if (_port?.IsOpen == true)
                return;
            // Mid-push: never Close+Open. Stock keeps one handle through prelude+C8.
            // A reopen here is what surfaces Device Error on the next Write.
            if (_pushInProgress != 0)
            {
                if (_port is not null)
                    return; // caller WriteChunk will surface real errors
                throw new IOException("Serial handle lost mid-push (no reopen during C8).");
            }
        }

        string? name;
        int baud;
        bool hadSession;
        lock (_portGate)
        {
            // Only auto-reopen while session is still active (Disconnect clears _sessionOpen).
            hadSession = _sessionOpen;
            name = _lastPortName;
            baud = _lastBaud > 0 ? _lastBaud : 115200;
        }
        if (!hadSession || string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Connect first.");

        CloseKeepingSession();
        // Preserve TX lockout across silent host reopen - only Connect/Open(resetTxLockout:true) clears it.
        Open(name!, baud, softReset: softResetOnReopen, resetTxLockout: false);
        lock (_portGate) _sessionOpen = true;
    }

    private void CloseKeepingSession()
    {
        SerialPortStream? port;
        lock (_portGate)
        {
            port = _port;
            _port = null;
            // keep _sessionOpen / _lastPortName
        }
        if (port is null) return;
        try { if (port.IsOpen) port.Close(); } catch { /* ignore */ }
        try { port.Dispose(); } catch { /* ignore */ }
    }

    private static void SoftResetLine(SerialPortStream port, int baudRate)
    {
        try
        {
            port.DtrEnable = false;
            port.RtsEnable = false;
            Thread.Sleep(80);
            port.DtrEnable = true;
            port.RtsEnable = true;
            port.BaudRate = baudRate;
            port.DataBits = 8;
            port.Parity = Parity.None;
            port.StopBits = StopBits.One;
            port.Handshake = Handshake.None;
            Thread.Sleep(40);
        }
        catch { /* best-effort */ }
    }

    private void SetWritePhase(string phase)
    {
        _writePhase = phase ?? "-";
    }

    private void EnsureWriteTrace()
    {
        if (_writeTracePath is not null) return;
        lock (_writeTraceGate)
        {
            if (_writeTracePath is not null) return;
            var dir = Path.Combine(AppStorage.Root, "traces");
            try { Directory.CreateDirectory(dir); } catch { /* ignore */ }
            var name = $"write-trace-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            _writeTracePath = Path.Combine(dir, name);
            try
            {
                File.AppendAllText(_writeTracePath,
                    $"# write-trace start {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} Asia/Shanghai\r\n" +
                    $"# FirstBulk={FirstBulkWriteChunkBytes} DefaultChunk={DefaultWriteChunkBytes} TxPath=RjcpWrite one-handle\r\n");
            }
            catch { /* ignore */ }
        }
    }

    private void TraceWrite(string line)
    {
        try
        {
            EnsureWriteTrace();
            var path = _writeTracePath;
            if (path is null) return;
            var stamped = $"{DateTime.Now:HH:mm:ss.fff} {line}\r\n";
            lock (_writeTraceGate)
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs);
                sw.Write(stamped);
                sw.Flush();
                fs.Flush(true);
            }
        }
        catch { /* never throw from trace */ }
    }

    public void WriteFrame(byte cmd, uint value, byte trailer, ReadOnlySpan<byte> payload = default,
        CancellationToken cancellationToken = default)
    {
        var frame = FrameFormat.Build(cmd, value, trailer, payload);
        WriteRaw(frame, DefaultWriteChunkBytes, progress: null, cancellationToken);
    }

    public void WriteControlFrameFlushed(byte cmd, uint value, byte trailer, ReadOnlySpan<byte> payload = default,
        CancellationToken cancellationToken = default)
    {
        var frame = FrameFormat.Build(cmd, value, trailer, payload);
        WriteExact(frame, cancellationToken);
    }

    public void WriteRaw(ReadOnlySpan<byte> data) =>
        WriteRaw(data, DefaultWriteChunkBytes, progress: null, CancellationToken.None);

    public void WriteRaw(ReadOnlySpan<byte> data, int chunkBytes) =>
        WriteRaw(data, chunkBytes, progress: null, CancellationToken.None);

    public void WriteRaw(ReadOnlySpan<byte> data, int chunkBytes, Action<long, long>? progress) =>
        WriteRaw(data, chunkBytes, progress, CancellationToken.None);

    public void WriteRaw(
        ReadOnlySpan<byte> data,
        int chunkBytes,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        if (data.IsEmpty) return;
        var port = RequireLivePort();
        WriteRawOnPort(port, data, chunkBytes, progress, cancellationToken);
    }

    /// <summary>
    /// Bulk write on an already-acquired handle. Does NOT call RequireLivePort/EnsureLiveOpen.
    /// PushStillExperimental must use this so prelude+C8 stay on one CDC handle.
    /// First chunk = FirstBulkWriteChunkBytes (matches SS C8#1 first WriteFile), then chunkBytes.
    /// </summary>
    private void WriteRawOnPort(
        SerialPortStream port,
        ReadOnlySpan<byte> data,
        int chunkBytes,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        if (data.IsEmpty) return;
        if (chunkBytes < 64) chunkBytes = 64;
        cancellationToken.ThrowIfCancellationRequested();

        var offset = 0;
        var total = data.Length;
        TraceWrite(
            $"BULK-START phase={_writePhase} total={total} mode=flat chunkBytes={chunkBytes} " +
            $"firstN={Math.Min(FirstBulkWriteChunkBytes, total)}");
        progress?.Invoke(0, total);
        var rented = new byte[Math.Max(chunkBytes, FirstBulkWriteChunkBytes)];
        try
        {
            while (offset < total)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var n = offset == 0
                    ? Math.Min(FirstBulkWriteChunkBytes, total - offset)
                    : Math.Min(chunkBytes, total - offset);
                data.Slice(offset, n).CopyTo(rented.AsSpan(0, n));
                _writeStreamOffset = offset;
                WriteChunk(port, rented, 0, n, cancellationToken);
                offset += n;
                _writeStreamOffset = offset;
                progress?.Invoke(offset, total);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            throw new OperationCanceledException("Serial port closed during write.", ex, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
                                                   ex.Message.Contains("not open", StringComparison.OrdinalIgnoreCase))
        {
            throw new OperationCanceledException("Serial port closed during write.", ex, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new IOException(
                $"Serial write timed out after {offset}/{total} bytes (WriteTimeout={SafeWriteTimeout(port)}ms). " +
                "Disconnect, Connect (soft DTR), retry.",
                ex);
        }
        catch (IOException ex) when (IsSemaphoreTimeout(ex))
        {
            throw new IOException(
                $"Serial write semaphore timeout after {offset}/{total} bytes. " +
                "Disconnect, wait 2s, Connect, retry.",
                ex);
        }
    }

    private void WriteExact(ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        if (data.IsEmpty) return;
        var port = RequireLivePort();
        WriteExactOnPort(port, data, cancellationToken);
    }

    private void WriteExactOnPort(SerialPortStream port, ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        if (data.IsEmpty) return;
        cancellationToken.ThrowIfCancellationRequested();
        var buf = data.ToArray();
        try
        {
            _writeStreamOffset = 0;
            WriteChunk(port, buf, 0, buf.Length, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            throw new OperationCanceledException("Serial port closed during write.", ex, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new IOException(
                $"Serial write timed out after 0/{buf.Length} bytes (WriteTimeout={SafeWriteTimeout(port)}ms).",
                ex);
        }
        catch (IOException ex) when (IsSemaphoreTimeout(ex))
        {
            throw new IOException(
                $"Serial write semaphore timeout after 0/{buf.Length} bytes.",
                ex);
        }
    }

    /// <summary>
    /// Single Write on an already-acquired port. Does NOT reopen.
    /// Lock out ONLY when Write throws Device Error. Ignoring IsOpen flaps mid-stream -
    /// Close+Open between chunks is what falsely locked us out on this adapter.
    /// </summary>
    private void WriteChunk(SerialPortStream port, byte[] buf, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfTxLockedOut();

        int btwBefore = -1;
        bool isOpenBefore = false;
        try { btwBefore = port.BytesToWrite; } catch { /* ignore */ }
        try { isOpenBefore = port.IsOpen; } catch { /* ignore */ }
        var phase = _writePhase;
        var streamOff = _writeStreamOffset;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        TraceWrite($"ENTER phase={phase} streamOff={streamOff} bufOff={offset} count={count} BTW={btwBefore} IsOpen={isOpenBefore}");

        try
        {
            // RJCP Write only, no Flush (Flush/WaitForEmpty can Device Error after ~500 ms
            // when the URB never completed). On a healthy panel the drain is about 18 MB/s.
            port.Write(buf, offset, count);
            sw.Stop();
            int btwAfter = -1;
            bool isOpenAfter = false;
            try { btwAfter = port.BytesToWrite; } catch { /* ignore */ }
            try { isOpenAfter = port.IsOpen; } catch { /* ignore */ }
            TraceWrite($"EXIT  phase={phase} streamOff={streamOff} count={count} ok ms={sw.ElapsedMilliseconds} BTW={btwAfter} IsOpen={isOpenAfter} via=RjcpWrite");
        }
        catch (IOException ex) when (IsDeviceError(ex))
        {
            sw.Stop();
            int btwAfter = -1;
            bool isOpenAfter = false;
            try { btwAfter = port.BytesToWrite; } catch { /* ignore */ }
            try { isOpenAfter = port.IsOpen; } catch { /* ignore */ }
            TraceWrite($"FAIL  phase={phase} streamOff={streamOff} count={count} DeviceError ms={sw.ElapsedMilliseconds} BTW={btwAfter} IsOpen={isOpenAfter} msg={ex.Message}");
            MarkTxLockedOut();
            throw new IOException(PanelNotAcceptingMessage, ex);
        }
        catch (Exception ex)
        {
            sw.Stop();
            int btwAfter = -1;
            bool isOpenAfter = false;
            try { btwAfter = port.BytesToWrite; } catch { /* ignore */ }
            try { isOpenAfter = port.IsOpen; } catch { /* ignore */ }
            TraceWrite($"FAIL  phase={phase} streamOff={streamOff} count={count} {ex.GetType().Name} ms={sw.ElapsedMilliseconds} BTW={btwAfter} IsOpen={isOpenAfter} msg={ex.Message}");
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private void MarkTxLockedOut()
    {
        bool fire = false;
        lock (_portGate)
        {
            if (!_txLockedOut)
            {
                _txLockedOut = true;
                fire = true;
            }
        }
        try { StopKeepAlive(); } catch { /* ignore */ }
        if (fire)
        {
            try { OnTxLockedOut?.Invoke(); } catch { /* ignore */ }
        }
    }

    private void ThrowIfTxLockedOut()
    {
        if (TxLockedOut)
            throw new IOException(PanelNotAcceptingMessage);
    }

    /// <summary>
    /// Confirm the panel actually accepts TX before we call it "ready".
    /// One CF keepalive frame, then require the host buffer to drain (healthy panel drains
    /// 250 B in &lt;5 ms), then settle and require the handle to stay open - a hung panel
    /// queues the write, then usbser drops the handle ~500 ms later (deferred Device Error).
    /// Returns null when healthy, otherwise a human-readable reason. Never locks out:
    /// a no-drain probe means the panel MCU is hung, which only a VBUS drop recovers.
    /// </summary>
    public string? ProbeTxHealth(CancellationToken cancellationToken = default, int drainTimeoutMs = 300, int settleMs = 700)
    {
        try
        {
            if (TxLockedOut)
                return PanelNotAcceptingMessage;

            SerialPortStream port;
            try { port = RequireLivePort(); }
            catch (IOException ex) when (ex.Message.Contains("locked out", StringComparison.OrdinalIgnoreCase))
            { return PanelNotAcceptingMessage; }
            catch (Exception ex)
            { return ex.Message; }

            var frame = FrameFormat.BuildKeepAlive();
            try
            {
                SetWritePhase("probe-CF");
                _writeStreamOffset = 0;
                WriteChunk(port, frame, 0, frame.Length, cancellationToken);
            }
            catch (IOException ex) when (IsDeviceError(ex) || ex.Message.Contains("locked out", StringComparison.OrdinalIgnoreCase))
            {
                return PanelNotAcceptingMessage;
            }

            if (!WaitForDrain(port, drainTimeoutMs, cancellationToken))
                return PanelNotRespondingMessage;

            SleepSliced(settleMs, cancellationToken);

            if (!IsPortOpen(port))
                return "Serial handle dropped during settle after probe. The panel accepted the write then the adapter closed - power-cycle the panel, then Connect again.";

            return null;
        }
        catch (OperationCanceledException)
        {
            return "cancelled";
        }
        catch (IOException ex) when (ex.Message.Contains("locked out", StringComparison.OrdinalIgnoreCase) || IsDeviceError(ex))
        {
            return PanelNotAcceptingMessage;
        }
        catch (Exception ex)
        {
            return IsDeviceError(ex) ? PanelNotAcceptingMessage : ex.Message;
        }
    }

    /// <summary>Poll BytesToWrite until it reaches 0. False on timeout or handle drop.</summary>
    private static bool WaitForDrain(SerialPortStream port, int timeoutMs, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int btw;
            bool open;
            try
            {
                btw = port.BytesToWrite;
                open = port.IsOpen;
            }
            catch
            {
                return false;
            }
            if (!open)
                return false;
            if (btw == 0)
                return true;
            if (sw.ElapsedMilliseconds >= timeoutMs)
                return false;
            Thread.Sleep(1);
        }
    }

    private static bool IsPortOpen(SerialPortStream port)
    {
        try { return port.IsOpen; } catch { return false; }
    }

    /// <summary>Sleep in small slices so Disconnect cancel stays responsive.</summary>
    private static void SleepSliced(int ms, CancellationToken cancellationToken)
    {
        if (ms <= 0) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var left = ms - (int)sw.ElapsedMilliseconds;
            if (left <= 0) break;
            Thread.Sleep(left > 20 ? 20 : left);
        }
    }

    /// <summary>Dry-run: single CF pulse + drain + settle. No prelude, no C8.</summary>
    public string? DryRunProbe(CancellationToken cancellationToken = default) =>
        ProbeTxHealth(cancellationToken);

    public static bool IsDeviceError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            var msg = e.Message ?? "";
            if (msg.Contains("Device Error", StringComparison.OrdinalIgnoreCase))
                return true;
            if (e is IOException io && ((io.HResult & 0xFFFF) == 31 || (io.HResult & 0xFFFF) == 6))
                return true;
        }
        return false;
    }

    private static int SafeWriteTimeout(SerialPortStream port)
    {
        try { return port.WriteTimeout; } catch { return -1; }
    }

    /// <summary>250-byte CF keepalive probe. null = ok, else error text.</summary>
    public string? ProbeKeepAlive(CancellationToken cancellationToken = default)
    {
        try
        {
            var port = RequireLivePort();
            var prev = port.WriteTimeout;
            try
            {
                try { port.WriteTimeout = 3_000; } catch { /* ignore */ }
                // Do not Discard* here - DiscardOutBuffer/InBuffer can hang on this adapter after the first write.
                var frame = FrameFormat.BuildKeepAlive();
                WriteChunk(port, frame, 0, frame.Length, cancellationToken);
                return null;
            }
            finally
            {
                try { port.WriteTimeout = prev; } catch { /* ignore */ }
            }
        }
        catch (OperationCanceledException)
        {
            return "cancelled";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// EXPERIMENTAL still push. One CDC handle for prelude + C8 (no mid-push reopen).
    /// Pre-builds the stock-sized C8 stream before any TX so there is no alloc gap after prelude.
    /// Prelude and C8 stream follow the panel's documented frame order.
    /// </summary>
    public void PushStillExperimental(
        byte[] bgraPixels,
        byte brightnessByte,
        bool sendPrelude = true,
        bool includeVideoPath = false,   // false = still-image prelude; true adds 64/6E/6E/78 on-device path frames
        string videoPath = "/mnt/UDISK/video/8.8s.mp4", // on-device theme path, used only when includeVideoPath is true
        bool onePage = true,             // retained for call-site compat; stock bank is always 11 pages
        bool skipHealthCheck = false,    // ignored - Connect's probe is the health gate
        int expectedHandleEpoch = -1,    // ignored - never gate on handle identity
        Action<long, long>? progress = null,
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        _ = skipHealthCheck;
        _ = expectedHandleEpoch;

        StopKeepAlive();
        ThrowIfTxLockedOut();

        const int frameBytes = 320 * 240 * 4;
        var fullFrame = bgraPixels is not null && bgraPixels.Length == (int)FrameFormat.FullFrameValue11Pages;
        if (bgraPixels is null || (bgraPixels.Length != frameBytes && !fullFrame))
            throw new ArgumentException(
                $"Expected {frameBytes} BGRA bytes (320x240) or {(int)FrameFormat.FullFrameValue11Pages} (440x1920 native).",
                nameof(bgraPixels));

        var pageCount = 11;
        var payloadValue = FrameFormat.FullFrameValue11Pages;
        var bulkTimeoutMs = 30_000;
        _ = onePage;

        var pixels = new byte[checked((int)payloadValue)];
        if (fullFrame)
        {
            Buffer.BlockCopy(bgraPixels, 0, pixels, 0, pixels.Length);
        }
        else
        {
            for (var i = 0; i < pageCount; i++)
                Buffer.BlockCopy(bgraPixels, 0, pixels, i * frameBytes, frameBytes);
        }
        var stream = FrameFormat.BuildFullFrame(pixels);

        PushStreamCore(stream, brightnessByte, sendPrelude, includeVideoPath, videoPath, pageCount, bulkTimeoutMs, prevTimeoutDummy: false, progress, status, cancellationToken);
    }

    /// <summary>Push a fully-formed 250-header + 249/250-bulk C8 stream with the stock prelude.</summary>
    public void PushRawStreamExperimental(
        byte[] stream3393250,
        byte brightnessByte,
        bool includeVideoPath = false,
        string videoPath = "/mnt/UDISK/video/8.8s.mp4",
        Action<long, long>? progress = null,
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (stream3393250 is null || stream3393250.Length != 3393250)
            throw new ArgumentException("Expected exactly 3393250 bytes (250-byte header + 13572 250-byte bulk buffers).", nameof(stream3393250));
        StopKeepAlive();
        ThrowIfTxLockedOut();
        PushStreamCore(stream3393250, brightnessByte, true, includeVideoPath, videoPath, 11, 30_000, false, progress, status, cancellationToken);
    }

    private void PushStreamCore(
        byte[] stream,
        byte brightnessByte,
        bool sendPrelude,
        bool includeVideoPath,
        string videoPath,
        int pageCount,
        int bulkTimeoutMs,
        bool prevTimeoutDummy,
        Action<long, long>? progress,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        status?.Invoke("Experimental: arming push (one handle, prelude + C8)...");
        ThrowIfTxLockedOut();
        EnsureLiveOpen(softResetOnReopen: false);
        SerialPortStream port;
        lock (_portGate)
            port = _port ?? throw new InvalidOperationException("Connect first.");
        if (port.IsOpen != true)
            throw new IOException("Serial handle not open before prelude.");

        if (Interlocked.Exchange(ref _pushInProgress, 1) == 1)
            throw new InvalidOperationException("Push already in progress.");

        string phase = "arm";
        int prevTimeout = 8000;
        try
        {
            try { prevTimeout = port.WriteTimeout; } catch { prevTimeout = 8000; }
            try { port.WriteTimeout = 8_000; } catch { /* ignore */ }

            if (sendPrelude)
            {
                // Still-image prelude, written back-to-back:
                // 01 79 96 7B 7D [64 6E 6E 78 if a video path is included] 86 2C 7B, then C8.
                // 0x87 is the disconnect/flush frame and is not sent here; sending it would
                // end the session. A single 0x01 starts the still prelude.
                phase = "prelude";
                SetWritePhase("prelude");
                TraceWrite("PUSH-START prelude begin still");
                status?.Invoke("Experimental: prelude (still: 01 79 96 7B 7D 86 2C 7B)...");
                WritePreludeFrameOnPort(port, 0x01, 1, 0, new byte[] { 0x00, 0x00, 0xC5, 0xD3 }, cancellationToken);
                WritePreludeFrameOnPort(port, 0x79, 1, 0, default, cancellationToken);
                WritePreludeFrameOnPort(port, 0x96, 1, 0, default, cancellationToken);
                WritePreludeFrameOnPort(port, 0x7B, 1, 0, new byte[] { 0x00, 0x00, brightnessByte, 0x00 }, cancellationToken);
                WritePreludeFrameOnPort(port, 0x7D, 5, 0, new byte[] { 0x00, 0x00, brightnessByte, 0x02 }, cancellationToken);
                if (includeVideoPath)
                {
                    // Path triad: tells the panel to play stored content from its on-device path.
                    // Do not send this for a plain still-image push, or the panel shows the
                    // stored theme instead of our pixels.
                    WritePreludeFrameOnPort(port, 0x64, 1, 0, default, cancellationToken);
                    var pathPayload = BuildVideoPathPayload(videoPath); // 00 00 + ASCII path
                    WritePreludeFrameOnPort(port, 0x6E, (uint)videoPath.Length, 0, pathPayload, cancellationToken);
                    WritePreludeFrameOnPort(port, 0x6E, (uint)videoPath.Length, 0, pathPayload, cancellationToken);
                    WritePreludeFrameOnPort(port, 0x78, (uint)videoPath.Length, 1, pathPayload, cancellationToken);
                }
                WritePreludeFrameOnPort(port, 0x86, 1, 0, default, cancellationToken);
                WritePreludeRawOnPort(port, Enumerable.Repeat((byte)0x2C, 250).ToArray(), cancellationToken);
                WritePreludeFrameOnPort(port, 0x7B, 1, 0, new byte[] { 0x00, 0x00, brightnessByte, 0x00 }, cancellationToken);
                status?.Invoke($"Experimental: prelude ok - sending C8 ({pageCount} page)...");
            }

            cancellationToken.ThrowIfCancellationRequested();

            phase = "C8";
            SetWritePhase("C8");
            try { port.WriteTimeout = Math.Max(prevTimeout, bulkTimeoutMs); } catch { /* ignore */ }
            // Same handle as prelude - WriteRawOnPort never calls EnsureLiveOpen.
            WriteRawOnPort(port, stream, DefaultWriteChunkBytes, progress, cancellationToken);
            status?.Invoke($"Experimental: C8 {pageCount}-page write finished");
        }
        catch (IOException ex) when (ex.Message.Contains("locked out", StringComparison.OrdinalIgnoreCase) || IsDeviceError(ex))
        {
            var full = $"Push failed during {phase}: {PanelNotAcceptingMessage}";
            TraceWrite($"PUSH-FAIL phase={phase} lockedOut/DeviceError msg={full} inner={ex.Message}");
            throw new IOException(full, ex);
        }
        catch (Exception ex) when (phase != "arm" && IsWriteStall(ex))
        {
            var full = $"Push failed during {phase}: {ex.Message}";
            TraceWrite($"PUSH-FAIL phase={phase} stall msg={full}");
            throw new IOException(full, ex);
        }
        finally
        {
            try
            {
                if (port is not null)
                {
                    try { port.WriteTimeout = prevTimeout; } catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
            Interlocked.Exchange(ref _pushInProgress, 0);
        }
    }

    private void WritePreludeFrameOnPort(SerialPortStream port, byte cmd, uint value, byte trailer,
        ReadOnlySpan<byte> payload, CancellationToken cancellationToken)
    {
        try
        {
            SetWritePhase($"prelude-0x{cmd:X2}");
            var frame = FrameFormat.Build(cmd, value, trailer, payload);
            WriteExactOnPort(port, frame, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsWriteStall(ex))
        {
            throw new IOException(ComWriteStalledMessage, ex);
        }
    }

    private void WritePreludeRawOnPort(SerialPortStream port, byte[] data, CancellationToken cancellationToken)
    {
        try
        {
            SetWritePhase("prelude-0x2C-pad");
            WriteExactOnPort(port, data, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsWriteStall(ex))
        {
            throw new IOException(ComWriteStalledMessage, ex);
        }
    }

    /// <summary>
    /// Stock sends 0xCF ~every 1.3 s while a session is live. We pace at ~800 ms so the
    /// handle does not sit idle after Connect's probe (idle after TX can surface Device Error).
    /// </summary>
    public void StartKeepAlive(int intervalMs = 800)
    {
        StopKeepAlive();
        if (intervalMs < 200) intervalMs = 200;
        // First tick ASAP so SyncConnectionStatus does not paint "Connection lost" before keepalive.
        _keepAliveTimer = new System.Threading.Timer(KeepAliveTick, null, 50, intervalMs);
    }

    public void StopKeepAlive()
    {
        var t = Interlocked.Exchange(ref _keepAliveTimer, null);
        try { t?.Dispose(); } catch { /* ignore */ }
    }

    private void KeepAliveTick(object? _)
    {
        if (Interlocked.Exchange(ref _keepAliveBusy, 1) == 1)
            return;
        if (Volatile.Read(ref _pushInProgress) != 0)
        {
            Interlocked.Exchange(ref _keepAliveBusy, 0);
            return;
        }
        try
        {
            if (TxLockedOut)
                return;

            bool session;
            lock (_portGate) session = _sessionOpen;
            if (!session)
                return;

            SerialPortStream? port;
            lock (_portGate) port = _port;
            // If the handle died, reopen quietly and keep pulsing - do not MarkTxLockedOut for a dead handle alone.
            if (port?.IsOpen != true)
            {
                try { EnsureLiveOpen(softResetOnReopen: false); }
                catch { return; }
                lock (_portGate) port = _port;
                if (port?.IsOpen != true)
                    return;
            }

            try
            {
                var frame = FrameFormat.BuildKeepAlive();
                SetWritePhase("keepalive");
                _writeStreamOffset = 0;
                WriteChunk(port, frame, 0, frame.Length, CancellationToken.None);
            }
            catch (Exception ex) when (IsDeviceError(ex))
            {
                // Immediate Device Error on Write - real TX failure; stop hammering.
                MarkTxLockedOut();
            }
            catch
            {
                // Transient; next tick retries after possible reopen.
            }
        }
        finally
        {
            Interlocked.Exchange(ref _keepAliveBusy, 0);
        }
    }

    public void RecoverAfterWriteFailure()
    {
        StopKeepAlive();
        SerialPortStream? port;
        lock (_portGate)
        {
            port = _port;
            _port = null;
            _sessionOpen = false;
            _handleEpoch++;
            _txLockedOut = true; // force a fresh Open+probe before any more TX
        }
        if (port is null) return;
        try { TryDiscardBuffers(port); } catch { /* ignore */ }
        try { if (port.IsOpen) port.Close(); } catch { /* ignore */ }
        try { port.Dispose(); } catch { /* ignore */ }
    }

    public void Disconnect()
    {
        StopKeepAlive();
        // 0x87 ends the host session so the panel returns to its onboard default image.
        // Skip it when TX is locked out - a write would only hammer a hung CDC.
        if (!TxLockedOut)
        {
            SerialPortStream? port;
            lock (_portGate) port = _port;
            if (port?.IsOpen == true)
            {
                try
                {
                    var prev = port.WriteTimeout;
                    try { port.WriteTimeout = 800; } catch { /* ignore */ }
                    try { WriteExact(FrameFormat.BuildFlush(), CancellationToken.None); } catch { /* ignore */ }
                    try { port.WriteTimeout = prev; } catch { /* ignore */ }
                }
                catch { /* ignore */ }
            }
        }
        Close();
    }

    public void Close()
    {
        StopKeepAlive();
        SerialPortStream? port;
        lock (_portGate)
        {
            port = _port;
            _port = null;
            _sessionOpen = false;
            _handleEpoch++;
        }
        if (port is null) return;
        try { if (port.IsOpen) port.Close(); } catch { /* ignore */ }
        try { port.Dispose(); } catch { /* ignore */ }
    }

    private static void TryDiscardBuffers(SerialPortStream port)
    {
        // Intentionally empty: DiscardIn/OutBuffer hang on this adapter after traffic starts.
        _ = port;
    }

    public static bool IsWriteStall(Exception ex) =>
        IsSemaphoreTimeout(ex) ||
        IsDeviceError(ex) ||
        ex is TimeoutException ||
        (ex is IOException && (
            ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("stalled", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("not accepting", StringComparison.OrdinalIgnoreCase)));

    public static bool IsSemaphoreTimeout(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            var msg = e.Message ?? "";
            if (msg.Contains("semaphore timeout", StringComparison.OrdinalIgnoreCase))
                return true;
            if (e is IOException io && (io.HResult & 0xFFFF) == 121)
                return true;
        }
        return false;
    }

    public void Dispose() => Close();
}
