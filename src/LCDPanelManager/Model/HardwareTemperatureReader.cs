using LibreHardwareMonitor.Hardware;

namespace LCDPanelManager.Model;

public sealed class HardwareTemperatureReader : IDisposable
{
    private Computer? _computer;

    public HardwareTemperatureReader()
    {
        try
        {
            _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            _computer.Open();
        }
        catch
        {
            _computer?.Close();
            _computer = null;
        }
    }

    public (double? Cpu, double? Gpu, double? GpuLoad) Read()
    {
        if (_computer is null) return (null, null, null);
        double? cpu = null, gpu = null, gpuLoad = null;
        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                Update(hardware);
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    cpu ??= ReadTemperature(hardware, cpu: true);
                }
                else if (hardware.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel)
                {
                    var discrete = IsDiscreteGpu(hardware);
                    if (gpu is null || discrete)
                    {
                        gpu = ReadTemperature(hardware, cpu: false) ?? gpu;
                        gpuLoad = ReadGpuLoad(hardware) ?? gpuLoad;
                    }
                }
            }
        }
        catch { }
        return (cpu, gpu, gpuLoad);
    }

    private static bool IsDiscreteGpu(IHardware hardware)
    {
        var name = hardware.Name;
        if (hardware.HardwareType == HardwareType.GpuNvidia) return true;
        if (hardware.HardwareType == HardwareType.GpuIntel)
            return name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
        if (hardware.HardwareType == HardwareType.GpuAmd)
            return name.Contains("RX", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Radeon VII", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static void Update(IHardware hardware)
    {
        hardware.Update();
        foreach (var child in hardware.SubHardware) Update(child);
    }

    private static double? ReadTemperature(IHardware hardware, bool cpu)
    {
        var sensors = hardware.Sensors
            .Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0)
            .ToList();
        if (sensors.Count == 0) return null;

        var preferred = cpu
            ? sensors.FirstOrDefault(s =>
                s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains("average", StringComparison.OrdinalIgnoreCase))
            : sensors.FirstOrDefault(s => s.Name.Contains("edge", StringComparison.OrdinalIgnoreCase));
        return (preferred ?? sensors[0]).Value;
    }

    private static double? ReadGpuLoad(IHardware hardware)
    {
        var sensors = hardware.Sensors
            .Where(s => s.SensorType == SensorType.Load && s.Value.HasValue)
            .ToList();
        var core = sensors.FirstOrDefault(s => s.Name.Contains("core", StringComparison.OrdinalIgnoreCase));
        var sensor = core ?? sensors.FirstOrDefault();
        return sensor?.Value is float value ? Math.Clamp((double)value, 0, 100) : null;
    }

    public void Dispose()
    {
        try { _computer?.Close(); } catch { }
        _computer = null;
    }
}
