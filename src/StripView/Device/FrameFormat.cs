namespace StripView.Device;

/// <summary>
/// Panel wire format: constants and pure frame builders.
/// Everything here works on byte arrays in memory; SerialPanelClient does the I/O.
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
    public const byte CmdFlush = 0x87;         // end host session; panel returns to onboard default image
    public const byte CmdClose = 0xCA;         // static close crumb; not used on the live disconnect path

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

    /// <summary>Full-frame payload size: 440*1920*4.</summary>
    public const uint FullFrameValue11Pages = FullFrameBytes;

    /// <summary>
    /// Full C8 wire stream: padded 250-byte header, then 250-byte buffers of 249 BGRA bytes + 1 zero pad.
    /// </summary>
    public static byte[] BuildFullFrame(ReadOnlySpan<byte> bgra440x1920)
    {
        if (bgra440x1920.Length != FullFrameBytes)
            throw new ArgumentException($"Expected {FullFrameBytes} BGRA bytes.", nameof(bgra440x1920));

        var stream = new byte[FullFrameWireBytes];
        Build(CmdFullFrame, FullFrameBytes, 0).CopyTo(stream, 0);
        var sourceOffset = 0;
        var destinationOffset = BufferSize;
        while (sourceOffset < bgra440x1920.Length)
        {
            var count = Math.Min(BulkDataBytes, bgra440x1920.Length - sourceOffset);
            bgra440x1920.Slice(sourceOffset, count).CopyTo(stream.AsSpan(destinationOffset, count));
            sourceOffset += count;
            destinationOffset += BufferSize;
        }
        return stream;
    }

    public static byte[] BuildFlush() => Build(CmdFlush, 1, 0);
    public static byte[] BuildClose() => Build(CmdClose, 0, 0);
    public static byte[] BuildKeepAlive() => Build(CmdKeepAlive, 1, 0);
    public static byte[] BuildFullFrameHeader() => Build(CmdFullFrame, FullFrameBytes, 0);
}
