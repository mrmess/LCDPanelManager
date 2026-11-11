# Contributing

## Build

You need the .NET 8 SDK. From the repository root:

```
build.cmd
run.cmd
```

`build.cmd` runs `dotnet` from `PATH`. If `dotnet` is not on `PATH` but
`D:\Tools\dotnet\dotnet.exe` exists, that copy is used. The published app is
self-contained and lands in `publish\win-x64`. The `publish\` folder is not
committed.

`LCDPanelManager.exe --render-frame out.png` renders the saved scene at 440x1920 and
exits without opening a serial port. The app requests administrator approval
because LibreHardwareMonitor needs it for CPU temperature on some systems.

## Hardware

Connect only happens when the user clicks Connect. The supported panel and the
refused TComas LG600 screen are listed under Supported hardware in `README.md`.
Do not loosen that lock. Close other case-LCD apps before connecting.

## Style

The UI and user-facing strings are English only. Keep vendor product names out
of the UI, window title, and executable strings.
