# LCD Panel Manager

Offline WPF (.NET 8) companion for a USB case LCD strip. There is no network
service or cloud dependency. Licensed under MIT; see `LICENSE` and
`THIRD_PARTY.md`.

The app icon (`src/LCDPanelManager/assets/lcdpanelmanager.ico`) and the default Aurora
background (`src/LCDPanelManager/assets/abstract-aurora.png`) are original artwork
for this project and are included under the same MIT license.

## Supported hardware

LCD Panel Manager talks only to the panel below, and only after you click Connect.

| | |
| --- | --- |
| Name (in the app) | Case LCD strip |
| USB vendor/product ID | `VID_0525&PID_A4A7` |
| USB product string | Gadget Serial v2.4 |
| Windows device name | USB Serial Device (often COM3) |
| Resolution | 440 x 1920 |
| Interface | USB CDC (`usbser`) |

If more than one matching device is present, COM3 is preferred. Close other
case-LCD or panel applications before connecting.

The USB descriptor does not report a retail model name or a stable serial
number. Identification is by the vendor/product ID above.

### Refused device

A different USB screen on the same machine must never be opened:

| | |
| --- | --- |
| Name | TComas LG600 screen |
| USB vendor/product ID | `VID_1A86&PID_CA11` |
| Example serial | `CT13INCH` (one unit) |
| Windows device name | USB Serial Device (often COM4) |

## Build and run

You need the .NET 8 SDK.

- `build.cmd` publishes a self-contained win-x64 app to `publish\win-x64`.
  It uses `dotnet` from `PATH`, and falls back to `D:\Tools\dotnet\dotnet.exe`
  if that file exists and `dotnet` is not on `PATH`.
- `run.cmd` starts `publish\win-x64\LCDPanelManager.exe`. Running that exe directly
  is the same app.
- No installed .NET Desktop Runtime is required by the published app.
- Administrator approval is required because LibreHardwareMonitor needs it
  for CPU temperature access on some systems (including Ryzen).
- `LCDPanelManager.exe --render-frame out.png` renders the saved scene at 440x1920
  and exits without opening a serial port.

See `CONTRIBUTING.md` for a short contributor guide.

## Tray and startup

The close button (X) and Minimize hide the window to the system tray; sensor
sampling and panel updates keep running. The tray menu contains Open, Start
minimized, Start with Windows, and Exit. Exit releases the panel back to its
onboard default image, then quits.

Both startup preferences are saved to `%LOCALAPPDATA%\LCDPanelManager\preferences.json`.
On first run, if that folder does not exist and `%LOCALAPPDATA%\StripView` does, the
old folder is moved so existing layouts, preferences, and traces are kept.
Start with Windows writes a current-user Run entry named `LCDPanelManager` and
removes a leftover `StripView` entry if one is present. It still triggers
the administrator prompt required for hardware temperature access.

## Panel updates

It supports Push once and recurring full-frame C8 updates. Brightness defaults
to `0x79` and is limited to `0x20`–`0xA0`.

## Layouts

Layouts are saved to `%LOCALAPPDATA%\LCDPanelManager\layout.json`.

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