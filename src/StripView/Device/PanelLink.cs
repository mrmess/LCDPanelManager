namespace StripView.Device;

public readonly record struct PushResult(bool Sent, string Message);

/// <summary>
/// The only place a send could ever start. In this build it is hard-off: it never opens,
/// probes, or writes a serial port. Writes need Donald's explicit go before this changes.
/// </summary>
public sealed class PanelLink
{
    public bool WritesAuthorized => false;
    public bool IsOpen => false;

    public PushResult PushOnce(PortInfo? target)
    {
        if (target is null)
            return new(false, $"No {DeviceIdentity.StripName} found ({DeviceIdentity.StripId}). Nothing was sent.");
        if (target.Verdict != PortVerdict.Target)
            return new(false, $"{target.PortName} is refused: {target.Reason}");
        return new(false, $"Frame rendered for {target.PortName}, not sent. Sending is off in this build.");
    }
}
