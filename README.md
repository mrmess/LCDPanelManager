# StripView

Offline WPF (.NET 8) app for the 440x1920 USB case LCD strip. No network, no cloud.

- Build: `build.cmd` (uses D:\Tools\dotnet\dotnet.exe; C:\Program Files\dotnet has no dotnet.exe host on BULL)
- Run:   `run.cmd` (sets DOTNET_ROOT=D:\Tools\dotnet, then starts bin\Debug\net8.0-windows\StripView.exe)
- Frame: `StripView.exe --render-frame out.png` renders the saved scene at 440x1920 and exits

This build never opens or writes a serial port. Push once and Send to panel only render a preview.
Layout is saved to %LOCALAPPDATA%\StripView\layout.json.
