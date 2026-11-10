using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StripView.Model;

namespace StripView.Controls;

/// <summary>Renders a layout offscreen at the panel's native 440x1920. This is the frame a push would send.</summary>
public static class StripRenderer
{
    public static BitmapSource Render(StripLayout layout, TelemetrySource telemetry)
    {
        var canvas = new StripCanvas { Width = StripLayout.StripWidth, Height = StripLayout.StripHeight };
        canvas.Attach(layout, telemetry);
        canvas.Measure(new Size(StripLayout.StripWidth, StripLayout.StripHeight));
        canvas.Arrange(new Rect(0, 0, StripLayout.StripWidth, StripLayout.StripHeight));
        canvas.UpdateLayout();
        canvas.RefreshValues();
        canvas.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)StripLayout.StripWidth, (int)StripLayout.StripHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(canvas);
        rtb.Freeze();
        canvas.Detach();
        return rtb;
    }

    /// <summary>BGRA bytes, row-major, 440*1920*4 = 3,379,200 bytes.</summary>
    public static byte[] RenderBgra(StripLayout layout, TelemetrySource telemetry)
    {
        var bmp = Render(layout, telemetry);
        var stride = (int)StripLayout.StripWidth * 4;
        var bytes = new byte[stride * (int)StripLayout.StripHeight];
        bmp.CopyPixels(bytes, stride, 0);
        return bytes;
    }

    public static void SavePng(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
