namespace StripView.Device;

/// <summary>
/// Panel wire format, carried over from the earlier offline app as constants and pure builders.
/// Everything here works on byte arrays in memory; nothing opens a port.
/// Layout: [cmd][0xEF][0x69][u32 big-endian value][trailer][payload], padded to 250-byte buffers.
/// </summary>
public static class FrameFormat
{
    public const int BufferSize = 250;
    public const byte Magic0 = 0xEF;
    public const byte Magic1 = 0x69;

    public const byte CmdFullFrame = 0xC8;     // full 440x1920 BGRA frame
    public const byte CmdPartialUpdate = 0xCC; // live/partial update
    public const byte CmdKeepAlive = 0xCF;     // value=1, about every 1.3 s
    public const byte CmdClose = 0xCA;

    public const int Width = 440;
    public const int Height = 1920;
    public const uint FullFrameBytes = (uint)(Width * Height * 4);           // 3,379,200
    public const int BulkDataBytes = BufferSize - 1;                   // 249 pixel bytes per buffer
    public const int FullFrameBuffers = 13572;                         // ceil(3,379,200 / 249)
    public const int FullFrameWireBytes = BufferSize + FullFrameBuffers * BufferSize; // 3,393,250

    public static byte[] Build(byte cmd, uint value, byte trailer, ReadOnlySpan<byte> payload = default)
    {
        if (payload.Length > BufferSize - 8)
            throw new ArgumentOutOfRangeException(nameof(payload));
        var buf = new byte[BufferSize];
        buf[0] = cmd;
        buf[1] = Magic0;
        buf[2] = Magic1;
        buf[3] = (byte)(value >> 24);
        buf[4] = (byte)(value >> 16);
        buf[5] = (byte)(value >> 8);
        buf[6] = (byte)value;
        buf[7] = trailer;
        payload.CopyTo(buf.AsSpan(8));
        return buf;
    }

    public static byte[] BuildKeepAlive() => Build(CmdKeepAlive, 1, 0);
    public static byte[] BuildFullFrameHeader() => Build(CmdFullFrame, FullFrameBytes, 0);
}
