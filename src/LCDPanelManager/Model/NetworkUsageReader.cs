using System.Diagnostics;
using System.Net.NetworkInformation;

namespace LCDPanelManager.Model;

public sealed class NetworkUsageReader : IDisposable
{
    private readonly List<PerformanceCounter> _down = new();
    private readonly List<PerformanceCounter> _up = new();

    public string AdapterName { get; private set; } = "No adapter";

    public NetworkUsageReader()
    {
        try
        {
            var category = new PerformanceCounterCategory("Network Interface");
            var instances = category.GetInstanceNames();
            var active = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            (n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211) &&
                            n.GetIPProperties().GatewayAddresses.Count > 0)
                .Select(n => NormalizeInstanceName(n.Description))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selected = instances.FirstOrDefault(name => active.Contains(NormalizeInstanceName(name)));
            if (selected is null) return;

            AdapterName = selected;
            _down.Add(new PerformanceCounter("Network Interface", "Bytes Received/sec", selected));
            _up.Add(new PerformanceCounter("Network Interface", "Bytes Sent/sec", selected));
            _ = _down[0].NextValue();
            _ = _up[0].NextValue();
        }
        catch
        {
            Dispose();
        }
    }

    private static string NormalizeInstanceName(string name) => name
        .Replace('(', '[')
        .Replace(')', ']')
        .Replace('/', '_')
        .Replace('\\', '_')
        .Replace('#', '_');

    public (double? Down, double? Up) Read()
    {
        double? down = Read(_down), up = Read(_up);
        return (down, up);
    }

    private static double? Read(IEnumerable<PerformanceCounter> counters)
    {
        var total = 0.0;
        var read = false;
        foreach (var counter in counters)
        {
            try
            {
                total += counter.NextValue();
                read = true;
            }
            catch { }
        }
        return read ? total * 8.0 / 1_000_000.0 : null;
    }

    public void Dispose()
    {
        foreach (var counter in _down) counter.Dispose();
        foreach (var counter in _up) counter.Dispose();
        _down.Clear();
        _up.Clear();
    }
}
