using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace StripView.Model;

public sealed record DriveSample(string Letter, double UsedPercent, double FreeGb, double TotalGb)
{
    public string FreeText => TelemetrySource.FormatGb(FreeGb) + " free";
}

/// <summary>
/// Host telemetry. CPU load, memory, network and drives are read locally from Windows.
/// CPU temperature and GPU values are sample data until a sensor library is wired in.
/// Nothing here touches the network; network numbers are local adapter counters.
/// </summary>
public sealed class TelemetrySource
{
    private readonly Random _rng = new(7);
    private long _idle, _kernel, _user;
    private long _rx, _tx;
    private DateTime _netAt;
    private readonly Queue<double> _cpuMinute = new();

    public double CpuPercent { get; private set; }
    public double CpuTrend { get; private set; }
    public double CpuTempC { get; private set; } = 52;
    public double GpuPercent { get; private set; } = 3;
    public double GpuTempC { get; private set; } = 43;
    public double MemPercent { get; private set; }
    public double MemUsedGb { get; private set; }
    public double MemTotalGb { get; private set; } = 32;
    public double DownMbps { get; private set; }
    public double UpMbps { get; private set; }
    public string NetAdapter { get; private set; } = "No adapter";
    public List<double> DownHistory { get; } = new();
    public List<double> UpHistory { get; } = new();
    public IReadOnlyList<DriveSample> Drives { get; private set; } = Array.Empty<DriveSample>();

    public int LiveSensorCount => 6;   // CPU load, memory, down, up, drive used, drive free
    public int SampleSensorCount => 3; // CPU temp, GPU load, GPU temp

    public void Sample()
    {
        SampleCpu();
        SampleMemory();
        SampleNetwork();
        SampleDrives();
        CpuTempC = Walk(CpuTempC, 38, 78, 1.5);
        GpuPercent = Walk(GpuPercent, 1, 60, 3);
        GpuTempC = Walk(GpuTempC, 35, 70, 1);
    }

    private double Walk(double v, double min, double max, double step) =>
        Math.Clamp(v + (_rng.NextDouble() * 2 - 1) * step, min, max);

    private void SampleCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return;
        if (_kernel != 0)
        {
            var dIdle = idle - _idle;
            var dTotal = (kernel - _kernel) + (user - _user);
            if (dTotal > 0) CpuPercent = Math.Clamp(100.0 * (1 - dIdle / (double)dTotal), 0, 100);
        }
        _idle = idle; _kernel = kernel; _user = user;
        _cpuMinute.Enqueue(CpuPercent);
        while (_cpuMinute.Count > 60) _cpuMinute.Dequeue();
        CpuTrend = CpuPercent - _cpuMinute.Peek();
    }

    private void SampleMemory()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref m)) return;
        MemTotalGb = m.TotalPhys / 1073741824.0;
        MemUsedGb = (m.TotalPhys - m.AvailPhys) / 1073741824.0;
        MemPercent = m.MemoryLoad;
    }

    private void SampleNetwork()
    {
        long rx = 0, tx = 0;
        string best = "No adapter";
        long bestBytes = -1;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = nic.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
                if (s.BytesReceived > bestBytes) { bestBytes = s.BytesReceived; best = nic.Description; }
            }
        }
        catch
        {
            return;
        }
        var now = DateTime.UtcNow;
        if (_netAt != default)
        {
            var sec = Math.Max(0.25, (now - _netAt).TotalSeconds);
            DownMbps = Math.Max(0, (rx - _rx) * 8 / 1e6 / sec);
            UpMbps = Math.Max(0, (tx - _tx) * 8 / 1e6 / sec);
        }
        _rx = rx; _tx = tx; _netAt = now;
        NetAdapter = best;
        Push(DownHistory, DownMbps);
        Push(UpHistory, UpMbps);
    }

    private static void Push(List<double> list, double v)
    {
        list.Add(v);
        if (list.Count > 60) list.RemoveAt(0);
    }

    private void SampleDrives()
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
        catch
        {
            // Keep the previous sample.
            return;
        }
        Drives = list;
    }

    public DriveSample? Drive(string? letter)
    {
        if (Drives.Count == 0) return null;
        var key = (letter ?? "").Trim().TrimEnd('\\');
        return Drives.FirstOrDefault(d => d.Letter.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? Drives[0];
    }

    public static string FormatGb(double gb) => gb >= 1000 ? $"{gb / 1024:0.00} TB" : $"{gb:0.0} GB";
    public static string FormatMbps(double v) => v >= 100 ? $"{v:0}" : v >= 10 ? $"{v:0.0}" : $"{v:0.00}";

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
}
