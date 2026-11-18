# Third-party notices

PiP Studio uses the following open-source software. Its license requires this notice to be included with copies of PiP Studio.

## WPF UI

- Project: https://github.com/lepoco/wpfui
- Package: [WPF-UI](https://www.nuget.org/packages/WPF-UI/) 3.0.5
- License: MIT

```
MIT License

Copyright (c) 2021-2024 Leszek Pomianowski and WPF UI Contributors. https://lepo.co/

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## QRCoder

- Project: https://github.com/codebude/QRCoder
- Package: [QRCoder](https://www.nuget.org/packages/QRCoder/) 1.6.0 (draws the QR code for pairing a phone)
- License: MIT

```
The MIT License (MIT)

Copyright (c) 2013-2018 Raffael Herrmann

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

## LibreHardwareMonitor and its libraries

Used only while PiP Studio runs as administrator, to read the processor's temperature and power and the fan speeds in
System details. Their source code is available at the links below.

- [LibreHardwareMonitorLib](https://www.nuget.org/packages/LibreHardwareMonitorLib/) 0.9.6, https://github.com/LibreHardwareMonitor/LibreHardwareMonitor: Mozilla Public License 2.0 (https://mozilla.org/MPL/2.0/)
- [DiskInfoToolkit](https://github.com/Blacktempel/DiskInfoToolkit): Mozilla Public License 2.0
- [RAMSPDToolkit-NDD](https://github.com/Blacktempel/RAMSPDToolkit): Mozilla Public License 2.0
- [HidSharp](https://software.seekye.com/hidsharp), Copyright 2010-2025 James F. Bellinger: Apache License 2.0 (https://www.apache.org/licenses/LICENSE-2.0)
- Mono.Posix.NETStandard, © Microsoft: MIT License

The sensors are read through the PawnIO driver (https://pawnio.eu), which PiP Studio doesn't include: people who want
these readings install it themselves.

## .NET

The self-contained downloads include the .NET 8 runtime and Windows Desktop runtime, © .NET Foundation and Contributors, under the MIT License: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
