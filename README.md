<div align="center">

<img src="docs/images/logo.png" width="96" alt="BogiHub logo">

# BogiHub

**Use the Steam Controller (2026) in any Windows game, without Steam.**

BogiHub turns the controller into a regular Xbox controller in games, and into a mouse and keyboard on the desktop,
automatically. It also shows a live hologram of every button, stick, trackpad and grip you touch.

[![Download](https://img.shields.io/github/v/release/OppaiMaster7/BogiHub?include_prereleases&label=download&color=5FD39A)](https://github.com/OppaiMaster7/BogiHub/releases/latest)
[![Build](https://img.shields.io/github/actions/workflow/status/OppaiMaster7/BogiHub/ci.yml?branch=main&label=tests)](https://github.com/OppaiMaster7/BogiHub/actions/workflows/ci.yml)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0A0C0F)](#install)
[![License: MIT](https://img.shields.io/badge/license-MIT-EEF1F5)](LICENSE)

<img src="docs/images/overview.png" alt="BogiHub overview page with the controller hologram" width="860">

</div>

> [!NOTE]
> **Early preview.** Everything here runs on real hardware, but in-game testing has only just started. See
> [what has been tested](docs/compatibility.md) and please [report what works for you](https://github.com/OppaiMaster7/BogiHub/issues/new/choose).

## Install

1. Download **`BogiHub-Setup-x.y.z.exe`** from the [latest release](https://github.com/OppaiMaster7/BogiHub/releases/latest).
2. Run it. No admin rights are needed for BogiHub itself, and no .NET install is needed: everything is included.
3. Leave **"Install the ViGEmBus driver"** ticked if you want gamepad mode (Windows asks for admin once).
   The box doesn't appear when the driver is already installed.
4. Turn on the controller. So far it has been tested with the wireless puck; USB-C and Bluetooth aren't tested yet.

> [!TIP]
> BogiHub isn't code-signed yet, so Windows SmartScreen may say *"Windows protected your PC"*. Click
> **More info → Run anyway**. Every release lists the installer's SHA-256 so you can check the download.

Updating: run the newer installer. It closes BogiHub for you and keeps your settings and profiles.
Uninstalling: *Settings → Apps → BogiHub*. It asks whether to keep your settings.

## What it does

<table>
<tr>
<td width="50%" valign="top">

**🎮 Gamepad in games, mouse on the desktop.** In **Auto** mode, any game (or fullscreen app) gets a virtual Xbox
controller and the desktop gets the controller's own mouse and keyboard. **Manual** mode picks one for everywhere.
**Hold "…" for 1.5 s** to flip between them at any time (two buzzes = gamepad, one long buzz = mouse).

**🛟 Gets along with Steam.** Steam keeps reading the controller while it runs. BogiHub shows who is in control,
blocks Steam's duplicate key presses, releases stuck keys, and can close Steam cleanly for you.

**🔔 Smooth on-screen notifications.** Who controls the controller, low battery, Steam getting in the way,
connect/disconnect. They slide in over games, never steal focus, and clicks go straight through them.

</td>
<td width="50%" valign="top">

<img src="docs/images/notifications.png" alt="BogiHub notifications" width="100%">

</td>
</tr>
</table>

**🕹️ Remap everything.** Every button, the four rear paddles, analog triggers, sticks (deadzones, sensitivity, as
keys or mouse), trackpads (trackball mouse, scroll, D-pad, virtual stick), touch sensors and basic gyro.

**📁 Per-game profiles.** Profiles switch automatically by game executable, or pin one manually. They're plain JSON
in `%APPDATA%\BogiHub\profiles` and can be imported and exported.

**🛑 Safe by design.** Emergency stop with **Ctrl+Shift+F12**, the sidebar button or the tray. Every held input is
released on pause, profile switch, disconnect and exit. BogiHub runs without admin rights and never writes firmware.

<p align="center">
  <img src="docs/images/controls.png" alt="Controls editor with every binding shown on the hologram" width="860">
</p>

## Steam and BogiHub at the same time

In games that aren't Steam games, Steam still applies its own **desktop layout** to the controller (by default X opens
Steam's keyboard, A/B/Y press Enter/Esc/Space) and keeps its **Steam-button shortcuts** live. BogiHub blocks Steam's
key presses and warns you, but Steam's own pop-ups only stop when Steam lets go of the controller. Either:

* exit Steam while you play (BogiHub's Overview has a button, and *Close Steam automatically* can do it for you), or
* in Steam → Settings → Controller, set the Desktop Layout and the Guide Button Chord Layout to a blank template.

## Known limitations

* Games running as administrator reject keyboard and mouse output (a Windows rule). The virtual controller isn't affected.
* Exclusive-fullscreen games draw over everything, so notifications only show in borderless and windowed games.
* ViGEmBus is retired upstream. It works on current Windows 10/11, but a future update could break it.
* Anti-cheat systems decide for themselves whether to accept virtual controllers. No game is guaranteed.

## Build from source

Requirements: Windows 10/11 x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
powershell -ExecutionPolicy Bypass -File scripts/fetch-deps.ps1   # SDL3 3.4.18 + ViGEmBus setup (checksum-verified)
dotnet test tests/BogiHub.Core.Tests
dotnet run --project src/BogiHub.App
```

Build the installer (needs [Inno Setup 6.7+](https://jrsoftware.org/isinfo.php): `winget install JRSoftware.InnoSetup`):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1   # → dist\BogiHub-Setup-<version>.exe
```

<details>
<summary>Project layout and hardware probe</summary>

```
src/BogiHub.Core               input model, mapping engine, profiles, control policy, notices (no OS code; unit-tested)
src/BogiHub.Devices.Sdl        SDL3 bindings + controller reader thread
src/BogiHub.Output             ViGEmBus virtual Xbox controller, SendInput keyboard/mouse
src/BogiHub.Platform.Windows   foreground window, hotkeys, injected-input monitor, Steam, autostart
src/BogiHub.Runtime            wires the pipeline together; diagnostics log
src/BogiHub.App                Avalonia UI, hologram, notifications, tray
installer/                     Inno Setup script
tools/BogiHub.Probe            hardware probe
tests/BogiHub.Core.Tests       xUnit
```

```powershell
dotnet run --project tools/BogiHub.Probe            # live view + coverage checklist, autosaves a report
dotnet run --project tools/BogiHub.Probe -- --list  # 3-second non-interactive check
dotnet run --project tools/BogiHub.Probe -- --vigem # plugs in a virtual Xbox pad for ~2 s and verifies it via XInput
dotnet run --project tools/BogiHub.Probe -- --guard # self-test of the Steam guard
```

</details>

## Credits

Built on [SDL](https://github.com/libsdl-org/SDL), [Avalonia](https://github.com/AvaloniaUI/Avalonia),
[ViGEmBus](https://github.com/nefarius/ViGEmBus) and [Tabler Icons](https://tabler.io/icons). Full list and licences:
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

BogiHub is MIT licensed. It's an independent project, not affiliated with or endorsed by Valve. "Steam" and
"Steam Controller" are trademarks of Valve Corporation; "Xbox" is a trademark of Microsoft.
