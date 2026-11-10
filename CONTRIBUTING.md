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

`StripView.exe --render-frame out.png` renders the saved scene at 440x1920 and
exits without opening a serial port. The app requests administrator approval
because LibreHardwareMonitor needs it for CPU temperature on some systems.

## Hardware

Connect only happens when the user clicks Connect. The send target is a USB
serial device with `VID_0525` and `PID_A4A7`. A device with `VID_1A86` and
`PID_CA11` (a TComas LG600 screen, often COM4 on some machines) is always
refused. Do not loosen that lock. Close other case-LCD apps before connecting.

## Style

The UI and user-facing strings are English only. Keep vendor product names out
of the UI, window title, and executable strings.
