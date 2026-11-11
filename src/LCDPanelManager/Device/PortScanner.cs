using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LCDPanelManager.Device;

public enum PortVerdict
{
    Target,
    Refused,
    NotOffered,
}

public sealed record PortInfo(string PortName, string? Vid, string? Pid, string? FriendlyName, PortVerdict Verdict, string Reason)
{
    public string UsbId => Vid is null ? "no USB ID" : $"VID_{Vid}&PID_{Pid}";
}

/// <summary>
/// Read-only COM enumeration from the registry. Never opens a port.
/// Present ports come from HARDWARE\DEVICEMAP\SERIALCOMM; each is matched to its USB VID/PID
/// through Enum\USB\...\Device Parameters\PortName, preferring instances the driver lists as present.
/// </summary>
public static class PortScanner
{
    private static readonly Regex IdRx = new(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<PortInfo> Scan()
    {
        var present = PresentPorts();
        var map = UsbPortMap();
        return present
            .OrderBy(PortNumber)
            .Select(p =>
            {
                map.TryGetValue(p, out var id);
                return Classify(p, id.Vid, id.Pid, id.Name);
            })
            .ToList();
    }

    /// <summary>The lockout rules. Order matters: refusals win over a match.</summary>
    public static PortInfo Classify(string port, string? vid, string? pid, string? name)
    {
        vid = vid?.ToUpperInvariant();
        pid = pid?.ToUpperInvariant();
        if (vid == DeviceIdentity.RefusedVid && pid == DeviceIdentity.RefusedPid)
            return new(port, vid, pid, name, PortVerdict.Refused, $"{DeviceIdentity.RefusedName}. Never a send target.");
        if (port.Equals(DeviceIdentity.RefusedPort, StringComparison.OrdinalIgnoreCase))
            return new(port, vid, pid, name, PortVerdict.Refused, $"{DeviceIdentity.RefusedPort} belongs to the {DeviceIdentity.RefusedName}. Refused whatever it reports.");
        if (vid == DeviceIdentity.StripVid && pid == DeviceIdentity.StripPid)
        {
            var where = port.Equals(DeviceIdentity.ExpectedPort, StringComparison.OrdinalIgnoreCase)
                ? "on its usual port"
                : $"matched by USB ID, but on {port} instead of {DeviceIdentity.ExpectedPort}";
            return new(port, vid, pid, name, PortVerdict.Target, $"{DeviceIdentity.StripName}, {where}.");
        }
        return new(port, vid, pid, name, PortVerdict.NotOffered,
            vid is null ? "No USB ID found. Not offered." : "Not the case LCD strip. Not offered.");
    }

    public static PortInfo? SelectTarget(IEnumerable<PortInfo> ports) => ports
        .Where(p => p.Verdict == PortVerdict.Target)
        .OrderBy(p => p.PortName.Equals(DeviceIdentity.ExpectedPort, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .FirstOrDefault();

    private static int PortNumber(string port) =>
        int.TryParse(new string(port.Where(char.IsDigit).ToArray()), out var n) ? n : 9999;

    private static List<string> PresentPorts()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (k is null) return new();
            return k.GetValueNames()
                .Select(n => k.GetValue(n) as string)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new();
        }
    }

    private static Dictionary<string, (string? Vid, string? Pid, string? Name)> UsbPortMap()
    {
        var best = new Dictionary<string, (string? Vid, string? Pid, string? Name, bool Present)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb is null) return new();
            foreach (var devId in usb.GetSubKeyNames())
            {
                var m = IdRx.Match(devId);
                if (!m.Success) continue;
                using var dev = usb.OpenSubKey(devId);
                if (dev is null) continue;
                foreach (var inst in dev.GetSubKeyNames())
                {
                    try
                    {
                        using var ik = dev.OpenSubKey(inst);
                        using var pk = ik?.OpenSubKey("Device Parameters");
                        var port = (pk?.GetValue("PortName") as string)?.Trim();
                        if (string.IsNullOrEmpty(port)) continue;
                        var name = CleanName(ik?.GetValue("FriendlyName") as string ?? ik?.GetValue("DeviceDesc") as string);
                        var present = IsPresent(ik?.GetValue("Service") as string, $@"USB\{devId}\{inst}");
                        if (best.TryGetValue(port, out var prev) && prev.Present && !present) continue;
                        best[port] = (m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.ToUpperInvariant(), name, present);
                    }
                    catch
                    {
                        // Some instance keys are not readable without admin; skip them.
                    }
                }
            }
        }
        catch
        {
            // Registry unavailable: report ports without identity.
        }
        return best.ToDictionary(kv => kv.Key, kv => (kv.Value.Vid, kv.Value.Pid, kv.Value.Name), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsPresent(string? service, string instancePath)
    {
        if (string.IsNullOrWhiteSpace(service)) return false;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service}\Enum");
            if (k is null) return false;
            return k.GetValueNames()
                .Select(n => k.GetValue(n) as string)
                .Any(v => string.Equals(v, instancePath, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static string? CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var i = raw.LastIndexOf(';');
        return i >= 0 ? raw[(i + 1)..].Trim() : raw.Trim();
    }
}
