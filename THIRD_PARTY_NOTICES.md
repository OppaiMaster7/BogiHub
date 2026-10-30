# Third-party notices

BogiHub is MIT licensed. It uses the following components:

| Component | Version | Licence | Use |
| --- | --- | --- | --- |
| [SDL](https://github.com/libsdl-org/SDL) | 3.4.18 | zlib | Reading the Steam Controller (HIDAPI Triton driver). Shipped as `SDL3.dll`. |
| [Nefarius.ViGEm.Client](https://github.com/nefarius/ViGEm.NET) | 1.21.256 | MIT | Talking to the ViGEmBus driver |
| [ViGEmBus](https://github.com/nefarius/ViGEmBus) | 1.22.0 | BSD-3-Clause | Virtual Xbox 360 controller driver. The installer carries the unmodified, signed official setup (`ViGEmBus_1.22.0_x64_x86_arm64.exe`) and runs it only if the user ticks the box. Not part of the app itself. Retired upstream. |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | 12.1.4 | MIT | User interface |
| [Inter](https://github.com/rsms/inter) (via Avalonia.Fonts.Inter) | — | SIL OFL 1.1 | UI font |
| [Tabler Icons](https://github.com/tabler/tabler-icons) | 3.49.0 | MIT | Line icons (sidebar, notifications, app and tray icon). Path data copied into `src/BogiHub.App/Theme/Icons.cs`. |
| [xUnit](https://github.com/xunit/xunit) | 2.9.3 | Apache-2.0 | Tests only |

"Steam" and "Steam Controller" are trademarks of Valve Corporation. BogiHub is an independent project,
not affiliated with or endorsed by Valve. "Xbox" is a trademark of Microsoft.

## Tabler Icons licence

```
MIT License

Copyright (c) 2020-2026 Paweł Kuna

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

## ViGEmBus licence

```
BSD 3-Clause License

Copyright (c) 2016-2020, Nefarius Software Solutions e.U.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
