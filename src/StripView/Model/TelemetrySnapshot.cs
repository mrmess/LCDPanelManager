namespace StripView.Model;

public sealed record TelemetrySnapshot
{
    public static readonly TelemetrySnapshot Empty = new();

    public double CpuPercent { get; init; }
    public double CpuTrend { get; init; }
    public double? CpuTempC { get; init; }
    public double? GpuPercent { get; init; }
    public double? GpuTempC { get; init; }
    public double MemPercent { get; init; }
    public double MemUsedGb { get; init; }
    public double MemTotalGb { get; init; } = 32;
    public double DownMbps { get; init; }
    public double UpMbps { get; init; }
    public string NetAdapter { get; init; } = "No adapter";
    public IReadOnlyList<double> DownHistory { get; init; } = Array.Empty<double>();
    public IReadOnlyList<double> UpHistory { get; init; } = Array.Empty<double>();
    public IReadOnlyList<DriveSample> Drives { get; init; } = Array.Empty<DriveSample>();
    public int LiveSensorCount { get; init; }

    public DriveSample? Drive(string? letter)
    {
        var key = (letter ?? "").Trim().TrimEnd('\\');
        return Drives.FirstOrDefault(d => d.Letter.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}
