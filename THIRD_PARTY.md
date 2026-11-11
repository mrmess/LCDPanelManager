# Third-party notices

LCD Panel Manager itself is licensed under the MIT License (see `LICENSE`). It includes
or depends on the software below. License names and copyright lines were taken
from each project's public repository or package metadata.

## RJCP.SerialPortStream 2.4.0

- Author: Jason Curl
- Copyright: (C) Jason Curl, 2012-2021
- License: Microsoft Public License (Ms-PL)
- Source: https://github.com/jcurl/RJCP.DLL.SerialPortStream
- In this tree: `src/LCDPanelManager/lib/RJCP.SerialPortStream.dll`

The full license text is in `third_party/RJCP.SerialPortStream-MS-PL.txt`.

## LibreHardwareMonitorLib 0.9.4

- License: Mozilla Public License 2.0
- Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- License text: https://www.mozilla.org/MPL/2.0/
- Used via the `LibreHardwareMonitorLib` NuGet package. This project does not
  modify that source. Upstream also lists additional notices in its
  `THIRD-PARTY-LICENSES` file.

LibreHardwareMonitor may load the WinRing0 kernel driver. Some security
products can flag or quarantine that driver; see the README.

## HidSharp (transitive, via LibreHardwareMonitor)

- Copyright 2010-2019 James F. Bellinger
- License: Apache License 2.0
- https://www.zer7.com/software.php?page=hidsharp
- License text: https://www.apache.org/licenses/LICENSE-2.0

A copy of `HidSharp.dll` is present in the self-contained publish output.

## Mono.Posix.NETStandard (transitive)

- Copyright (c) Mono Project
- License: MIT
- https://github.com/mono/mono.posix

A copy of `Mono.Posix.NETStandard.dll` is present in the self-contained
publish output (brought in by LibreHardwareMonitor).

## System.Diagnostics.PerformanceCounter 8.0.0

- Copyright (c) .NET Foundation and Contributors
- License: MIT
- Used via NuGet. https://github.com/dotnet/runtime
