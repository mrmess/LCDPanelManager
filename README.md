# StripView

Offline WPF (.NET 8) companion for a 440x1920 USB case LCD strip. There is no
network service or cloud dependency. Licensed under MIT; see `LICENSE` and
`THIRD_PARTY.md`.

The app icon (`src/StripView/assets/stripview.ico`) and the default Aurora
background (`src/StripView/assets/abstract-aurora.png`) are original artwork
for this project and are included under the same MIT license.

## Build and run

You need the .NET 8 SDK.

- `build.cmd` publishes a self-contained win-x64 app to `publish\win-x64`.
  It uses `dotnet` from `PATH`, and falls back to `D:\Tools\dotnet\dotnet.exe`
  if that file exists and `dotnet` is not on `PATH`.
- `run.cmd` starts `publish\win-x64\StripView.exe`.
- No installed .NET Desktop Runtime is required by the published app.
- Administrator approval is required because LibreHardwareMonitor needs it
  for CPU temperature access on some systems (including Ryzen).
- `StripView.exe --render-frame out.png` renders the saved scene at 440x1920
  and exits without opening a serial port.

See `CONTRIBUTING.md` for a short contributor guide.

## Tray and startup

The system-tray menu contains Open, Start minimized, Start with Windows, and Exit.
Minimizing hides the window but leaves sensor sampling and panel updates running.
Both startup preferences are saved to `%LOCALAPPDATA%\StripView\preferences.json`.
Start with Windows uses the current user's Run registry entry and still triggers
the administrator prompt required for hardware temperature access.

## Panel access

The app opens and writes to the case LCD only after an explicit Connect. The
send target is a USB serial device with `VID_0525` and `PID_A4A7` (often COM3).
If more than one matching device is present, COM3 is preferred. A device with
`VID_1A86` and `PID_CA11` (a TComas LG600 screen, often COM4) is always
refused. Close other case-LCD or panel applications before connecting.

It supports Push once and recurring full-frame C8 updates. Brightness defaults
to `0x79` and is limited to `0x20`–`0xA0`.

## Layouts

Layouts are saved to `%LOCALAPPDATA%\StripView\layout.json`.

- A readable saved layout is preserved, including its theme, widget positions,
  sizes, and text.
- Missing fields receive defaults individually.
- An older file is backed up to `layout.json.v1.bak` without replacing its
  arrangement.
- An unreadable file is moved to an `layout.json.unreadable-<timestamp>.bak`
  file before the default is used, so a later save cannot destroy the only copy.
- The Aurora default is used only when no valid saved file exists or when
  Reset to default layout is explicitly confirmed in Design. Default drive
  widgets are created for the fixed drives present on that machine.

## Sensors and security software

Sensor sampling runs on a background thread and produces immutable snapshots
for the UI and renderer. GPU load and temperature come from the discrete GPU
through LibreHardwareMonitor rather than a static list of GPU performance
counters.

LibreHardwareMonitor 0.9.4 may use the WinRing0 kernel driver. Some security
products, including Microsoft Defender, can flag or quarantine this driver.
If CPU or GPU temperatures remain `n/a`, check administrator access and the
security software's quarantine/history before troubleshooting the application.
Unavailable sensors are shown as `n/a`, not fabricated values.
