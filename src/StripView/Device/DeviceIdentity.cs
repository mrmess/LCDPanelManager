namespace StripView.Device;

/// <summary>Who StripView may talk to, and who it must refuse.</summary>
public static class DeviceIdentity
{
    // The case LCD strip (USB CDC). Normally enumerates as COM3.
    public const string StripVid = "0525";
    public const string StripPid = "A4A7";
    public const string ExpectedPort = "COM3";
    public const string StripName = "Case LCD strip";

    // A different USB screen (TComas LG600) that lives on COM4. Never a target.
    public const string RefusedVid = "1A86";
    public const string RefusedPid = "CA11";
    public const string RefusedPort = "COM4";
    public const string RefusedName = "TComas LG600 screen";

    public static string StripId => $"VID_{StripVid}&PID_{StripPid}";
    public static string RefusedId => $"VID_{RefusedVid}&PID_{RefusedPid}";
}
