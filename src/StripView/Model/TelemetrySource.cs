using System.IO;
using System.Runtime.InteropServices;

namespace StripView.Model;

public sealed record DriveSample(string Letter, double UsedPercent, double FreeGb, double TotalGb)
{
    public string FreeText => TelemetrySource.FormatGb(FreeGb) + " free";
}

/// <summary>Produces immutable telemetry snapshots away from the UI thread.</summary>
public sealed class TelemetrySource : IDisposable
{
    private readonly object _sync = new();
    private HardwareTemperatureReader? _temperatures;
    private NetworkUsageReader? _network;
    private readonly Queue<double> _cpuMinute = new();
    private readonly List<double> _downHistory = new();
    private readonly List<double> _upHistory = new();
    private long _idle, _kernel, _user;
    private double _memTotalGb = 32;
    private double _memUsedGb;
    private double _memPercent;
    private bool _disposed;

    public TelemetrySnapshot Sample()
    {
        lock (_sync)
        {
            if (_disposed) return TelemetrySnapshot.Empty;
            _temperatures ??= new HardwareTemperatureReader();
            _network ??= new NetworkUsageReader();
            var (cpuPercent, cpuTrend) = ReadCpu();
            ReadMemory();
            var (cpuTemp, gpuTemp, gpuLoad) = _temperatures.Read();
            var (down, up) = _network.Read();
            if (down is double downValue)
            {
                Push(_downHistory, downValue);
                Push(_upHistory, up ?? 0);
            }
            var drives = ReadDrives();
            var networkAvailable = down.HasValue || up.HasValue;
            var liveCount = 4 + (networkAvailable ? 2 : 0) +
                            (cpuTemp.HasValue ? 1 : 0) +
                            (gpuLoad.HasValue ? 1 : 0) +
                            (gpuTemp.HasValue ? 1 : 0);

            return new TelemetrySnapshot
            {
                CpuPercent = cpuPercent,
                CpuTrend = cpuTrend,
                CpuTempC = cpuTemp,
                GpuPercent = gpuLoad,
                GpuTempC = gpuTemp,
                MemPercent = _memPercent,
                MemUsedGb = _memUsedGb,
                MemTotalGb = _memTotalGb,
                DownMbps = down ?? 0,
                UpMbps = up ?? 0,
                NetAdapter = _network.AdapterName,
                DownHistory = _downHistory.ToArray(),
                UpHistory = _upHistory.ToArray(),
                Drives = drives,
                LiveSensorCount = liveCount,
            };
        }
    }

    private (double Percent, double Trend) ReadCpu()
    {
        var percent = 0.0;
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return (0, 0);
        if (_kernel != 0)
        {
            var dIdle = idle - _idle;
            var dTotal = (kernel - _kernel) + (user - _user);
            if (dTotal > 0) percent = Math.Clamp(100.0 * (1 - dIdle / (double)dTotal), 0, 100);
        }
        _idle = idle;
        _kernel = kernel;
        _user = user;
        _cpuMinute.Enqueue(percent);
        while (_cpuMinute.Count > 60) _cpuMinute.Dequeue();
        return (percent, percent - _cpuMinute.Peek());
    }

    private void ReadMemory()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref m)) return;
        _memTotalGb = m.TotalPhys / 1073741824.0;
        _memUsedGb = (m.TotalPhys - m.AvailPhys) / 1073741824.0;
        _memPercent = m.MemoryLoad;
    }

    private static IReadOnlyList<DriveSample> ReadDrives()
    {
        var list = new List<DriveSample>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                var total = d.TotalSize / 1073741824.0;
                var free = d.TotalFreeSpace / 1073741824.0;
                list.Add(new DriveSample(d.Name.TrimEnd('\\'), total > 0 ? 100 * (1 - free / total) : 0, free, total));
            }
        }
        catch { }
        return list;
    }

    private static void Push(List<double> list, double value)
    {
        list.Add(value);
        if (list.Count > 60) list.RemoveAt(0);
    }

    public static string FormatGb(double gb) => gb >= 1000 ? $"{gb / 1024:0.00} TB" : $"{gb:0.0} GB";
    public static string FormatMbps(double v) => v >= 100 ? $"{v:0}" : v >= 10 ? $"{v:0.0}" : $"{v:0.00}";
    public static string FormatTemperature(double? value) => value is double temperature ? $"{temperature:0}°C" : "n/a";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _temperatures?.Dispose();
            _network?.Dispose();
        }
    }
}
