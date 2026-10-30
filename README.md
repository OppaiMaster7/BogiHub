<div align="center">

<img src="docs/images/logo.png" width="96" alt="BogiHub logo">

# BogiHub

**Use your Steam Controller in any Windows game. No Steam needed.**

<a href="https://github.com/OppaiMaster7/BogiHub/releases/download/v0.1.0/BogiHub-Setup-0.1.0.exe"><img src="docs/images/download-button.png" width="600" alt="Download BogiHub for Windows"></a>

Version <!--version-->0.1.0<!--/version--> (early preview) &nbsp;·&nbsp; <!--size-->54 MB<!--/size--> &nbsp;·&nbsp; Windows 10 and 11<br>
The button downloads **BogiHub-Setup-0.1.0.exe** straight to your Downloads folder.

**[Install](#install)** &nbsp;·&nbsp; **[How to use it](#how-to-use-it)** &nbsp;·&nbsp; **[What it does](#what-it-does)** &nbsp;·&nbsp; **[Steam](#playing-with-steam-open)** &nbsp;·&nbsp; **[Help](#help)** &nbsp;·&nbsp; **[Update or remove](#update-or-remove)**

<img src="docs/images/overview.png" alt="BogiHub showing a live picture of the controller" width="860">

</div>

BogiHub makes the controller a normal Xbox controller in your games, and a mouse and keyboard on the desktop. It
switches between the two by itself, and shows a live picture of every button, stick, trackpad and grip you touch.

> [!NOTE]
> **Early preview.** Everything runs on the real controller, but testing inside games has only just started. See
> [what has been tested](docs/compatibility.md), and please [tell me what works for you](https://github.com/OppaiMaster7/BogiHub/issues/new/choose).

## Install

It takes about a minute.

1. Click the big green **Download BogiHub** button above. Your browser saves **BogiHub-Setup-0.1.0.exe** in your
   **Downloads** folder. If the browser says the file isn't commonly downloaded, choose **Keep**.
2. Open **BogiHub-Setup-0.1.0.exe**. If Windows says **Windows protected your PC**, click **More info**, then
   **Run anyway**. Windows shows this for new apps that aren't signed yet.
3. Click **Next**, then **Next** again.
4. Leave **Install the ViGEmBus driver** ticked. It's what lets games see an Xbox controller. (You only see this box
   the first time.)
5. Click **Install**. When Windows asks if the driver may make changes, click **Yes**.
6. Click **Finish**. BogiHub opens.
7. Turn on your controller. BogiHub shows it live, and that's it: open any game and play.

<details>
<summary><b>Where did it install? Which file do I open later?</b></summary>
<br>

BogiHub is in your **Start menu** and, if you kept the box ticked, on your **desktop**. The download,
**BogiHub-Setup-0.1.0.exe**, is only the installer: you can delete it after installing.

</details>

<details>
<summary><b>Want to check the download is the real file?</b></summary>
<br>

Open **PowerShell** in your Downloads folder and run `Get-FileHash .\BogiHub-Setup-0.1.0.exe`. The hash it prints
should be <!--sha256-->`0C3FF6E3A4787F8E24CA54D209D268E3EBDF4E961F4B454E86FB3CE108B3BEB3`<!--/sha256-->.

</details>

## How to use it

| To do this | Do this |
| :--- | :--- |
| **Play a game** | Just start it. BogiHub gives the game an Xbox controller by itself. |
| **Use the desktop** | Nothing to do. Outside games, the controller is your mouse and keyboard. |
| **Switch right now** | Hold the **…** button (between the two trackpads) for 1.5 seconds. Two buzzes means gamepad, one long buzz means mouse and keyboard. |
| **Stop everything at once** | Press **Ctrl+Shift+F12**, or click **Emergency stop** in BogiHub. Every held button is let go. |
| **Change what a button does** | Open **Controls** in BogiHub and click that button on the picture of the controller. |
| **Use different controls for each game** | Open **Profiles**. A game's profile turns on by itself when the game starts. |

## What it does

<table>
<tr>
<td width="50%" valign="top">

**🎮 Gamepad in games, mouse on the desktop.** In **Auto** mode every game (or fullscreen app) gets a virtual Xbox
controller, and the desktop gets the controller's own mouse and keyboard. **Manual** mode uses one of them everywhere.

**🛟 Works next to Steam.** BogiHub shows who is in control, blocks Steam's extra key presses, lets go of stuck keys,
and can close Steam for you.

**🔔 Clear notifications.** Who is in control, low battery, Steam getting in the way, connected or not. They slide in
over your game, never take focus, and let clicks pass through.

</td>
<td width="50%" valign="top">

<img src="docs/images/notifications.png" alt="BogiHub notifications" width="100%">

</td>
</tr>
</table>

**🕹️ Change any control.** Every button, the four rear paddles, the triggers, both sticks (dead zones, speed, or as
keys or a mouse), both trackpads (trackball mouse, scrolling, a direction pad or a stick), the touch sensors and basic
gyro.

**📁 A profile for each game.** Profiles switch by themselves when a game starts, or you pick one yourself. They are
plain files in `%APPDATA%\BogiHub\profiles` that you can import and export.

**🛑 Safe.** The emergency stop (**Ctrl+Shift+F12**, the button in BogiHub or the tray icon) lets go of everything.
So do pausing, switching profiles, disconnecting and quitting. BogiHub runs without admin rights and never changes the
controller's firmware.

<p align="center">
  <img src="docs/images/controls.png" alt="The Controls page with every button's job shown on the controller" width="860">
</p>

## Playing with Steam open

Even in games that aren't on Steam, a running Steam still uses the controller in its own way: by default **X** opens
Steam's keyboard, **A**, **B** and **Y** press Enter, Esc and Space, and the Steam button shortcuts stay on. BogiHub
blocks those key presses and warns you, but Steam's own pop ups only stop when Steam lets go of the controller. Pick
one of these:

1. **Close Steam while you play.** BogiHub's Overview page has an **Exit Steam** button, and **Close Steam
   automatically** does it for you every time.
2. **Keep Steam open but quiet.** In Steam, open **Settings** > **Controller** and set both the **Desktop Layout** and
   the **Guide Button Chord Layout** to a blank template.

## Help

<details>
<summary><b>Windows says "Windows protected your PC".</b></summary>
<br>

Click **More info**, then **Run anyway**. Windows shows this for new apps that aren't signed yet. If you want to be
sure the file is the real one, check its hash (see [Install](#install)).

</details>

<details>
<summary><b>My game doesn't react to the controller.</b></summary>
<br>

Open BogiHub's **Overview** page while the game is running. The **Who's in control** card should say **Gamepad**.
If it says **Mouse & keyboard**, hold the **…** button for 1.5 seconds. If gamepad mode can't turn on at all, the ViGEmBus driver is missing: run the
installer again and leave **Install the ViGEmBus driver** ticked.

</details>

<details>
<summary><b>Steam's keyboard pops up, or Steam reacts to my buttons.</b></summary>
<br>

Steam is still using the controller. See [Playing with Steam open](#playing-with-steam-open).

</details>

<details>
<summary><b>The mouse and keyboard don't work in one game.</b></summary>
<br>

That game is running as administrator, and Windows blocks other apps' mouse and keyboard input there. The Xbox
controller still works in it.

</details>

<details>
<summary><b>I don't see notifications in my game.</b></summary>
<br>

Games in exclusive fullscreen draw over everything else. Switch the game to **borderless** or **windowed** and they
show up.

</details>

<details>
<summary><b>Will it work in games with anti cheat?</b></summary>
<br>

Each game decides for itself whether it accepts a virtual controller, and that can change with any update, so no
game is guaranteed. BogiHub only uses normal Windows input, and never touches a game's memory.

</details>

<details>
<summary><b>Something else is wrong.</b></summary>
<br>

[Open an issue](https://github.com/OppaiMaster7/BogiHub/issues/new/choose) and tell me what happened. It helps a lot
to attach BogiHub's log: in BogiHub open **Settings** > **Diagnostics** > **Export log**, then drag the file into the
issue.

</details>

**Known limits right now.** Tested with the wireless puck so far; the USB cable and Bluetooth aren't tested yet.
ViGEmBus, the driver behind gamepad mode, is no longer developed by its makers: it works on current Windows 10 and 11,
but a future Windows update could break it.

## Update or remove

**To update**, download the new installer with the button above and run it. It closes BogiHub for you and keeps your
settings and profiles.

**To remove BogiHub**, open Windows **Settings** > **Apps**, find **BogiHub** and choose **Uninstall**. It asks whether
to keep your settings.

## Build it yourself

<details>
<summary>Commands, project layout and the hardware probe</summary>
<br>

You need Windows 10 or 11 (64 bit) and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
powershell -ExecutionPolicy Bypass -File scripts/fetch-deps.ps1   # SDL3 3.4.18 and the ViGEmBus setup, checksum verified
dotnet test tests/BogiHub.Core.Tests
dotnet run --project src/BogiHub.App
```

To build the installer you also need [Inno Setup 6.7 or newer](https://jrsoftware.org/isinfo.php)
(`winget install JRSoftware.InnoSetup`). The script writes `dist\BogiHub-Setup-<version>.exe` and points this page's
download button at that version, so commit README.md together with the release.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1
```

```
src/BogiHub.Core               input model, mapping engine, profiles, control policy, notices (no OS code; unit tested)
src/BogiHub.Devices.Sdl        SDL3 bindings and the controller reader thread
src/BogiHub.Output             ViGEmBus virtual Xbox controller, SendInput keyboard and mouse
src/BogiHub.Platform.Windows   foreground window, hotkeys, injected input monitor, Steam, autostart
src/BogiHub.Runtime            wires the pipeline together; diagnostics log
src/BogiHub.App                Avalonia UI, hologram, notifications, tray
installer/                     Inno Setup script
tools/BogiHub.Probe            hardware probe
tests/BogiHub.Core.Tests       xUnit
```

```powershell
dotnet run --project tools/BogiHub.Probe            # live view and coverage checklist, saves a report
dotnet run --project tools/BogiHub.Probe -- --list  # 3 second check, no input needed
dotnet run --project tools/BogiHub.Probe -- --vigem # plugs in a virtual Xbox pad for about 2 s and checks it through XInput
dotnet run --project tools/BogiHub.Probe -- --guard # self test of the Steam guard
```

</details>

## Code signing policy

BogiHub has applied to the [SignPath Foundation](https://signpath.org/) open-source code signing program.
**Approval is pending; the current v0.1.0 preview is unsigned.** Once approved and integrated into the release
process, official Windows releases are planned to use free code signing provided by
[SignPath.io](https://about.signpath.io/), with a certificate from the [SignPath Foundation](https://signpath.org/).

See the [Code signing policy](docs/signing.md) for signing scope, maintainer roles and privacy information.
BogiHub remains licensed under the [MIT License](LICENSE).

## Credits

Built on [SDL](https://github.com/libsdl-org/SDL), [Avalonia](https://github.com/AvaloniaUI/Avalonia),
[ViGEmBus](https://github.com/nefarius/ViGEmBus) and [Tabler Icons](https://tabler.io/icons). The full list and their
licences are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

BogiHub is MIT licensed. It's an independent project, not affiliated with or endorsed by Valve. "Steam" and "Steam
Controller" are trademarks of Valve Corporation; "Xbox" is a trademark of Microsoft.
