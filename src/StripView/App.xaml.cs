using System.IO;
using System.Windows;
using StripView.Controls;
using StripView.Model;

namespace StripView;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // StripView.exe --render-frame out.png : render the saved scene at 440x1920 and exit. No window, no port.
        var i = Array.IndexOf(e.Args, "--render-frame");
        if (i >= 0)
        {
            var path = i + 1 < e.Args.Length ? e.Args[i + 1] : "strip-frame.png";
            var telemetry = new TelemetrySource();
            _ = Task.Run(() => telemetry.Sample()).GetAwaiter().GetResult();
            Thread.Sleep(600);
            var snapshot = Task.Run(() => telemetry.Sample()).GetAwaiter().GetResult();
            StripRenderer.SavePng(StripRenderer.Render(StripLayout.LoadOrDefault(), snapshot), Path.GetFullPath(path));
            telemetry.Dispose();
            Shutdown(0);
            return;
        }
        var window = new MainWindow();
        window.Show();
        if (window.StartHidden) window.HideToTray();
    }
}
