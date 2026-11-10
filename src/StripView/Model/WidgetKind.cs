namespace StripView.Model;

public enum WidgetKind
{
    Cpu,
    Gpu,
    GpuTemp,
    Memory,
    Drive,
    Network,
    Clock,
    Text,
    Image,
}

public sealed record WidgetInfo(WidgetKind Kind, string Label, double DefaultHeight, bool IsSample);

public static class WidgetCatalog
{
    public static readonly IReadOnlyList<WidgetInfo> All = new[]
    {
        new WidgetInfo(WidgetKind.Cpu, "CPU", 380, false),
        new WidgetInfo(WidgetKind.Gpu, "GPU", 380, true),
        new WidgetInfo(WidgetKind.GpuTemp, "GPU temp", 220, true),
        new WidgetInfo(WidgetKind.Memory, "Memory", 130, false),
        new WidgetInfo(WidgetKind.Drive, "Drive free", 130, false),
        new WidgetInfo(WidgetKind.Network, "Network", 260, false),
        new WidgetInfo(WidgetKind.Clock, "Clock", 200, false),
        new WidgetInfo(WidgetKind.Text, "Text", 110, false),
        new WidgetInfo(WidgetKind.Image, "Image", 300, false),
    };

    public static WidgetInfo Get(WidgetKind kind) => All.First(w => w.Kind == kind);
}
