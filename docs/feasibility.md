# Phase 0 feasibility report

Date: 29 October 2026 · Tester: project owner · Machine: Windows 11 Home (build 26300)

## 1. Hardware identified

| Item | Finding | How |
| --- | --- | --- |
| Controller | Steam Controller (2026, SDL codename "Triton") | SDL 3.4.18 driver name + USB descriptors |
| Receiver | "Steam Controller Puck", USB `28DE:1304` (SDL: *Proteus dongle*) | Windows PnP `BusReportedDeviceDesc` |
| Puck interfaces | MI_00 CDC serial; MI_02–MI_05 controller slots (vendor HID + built-in keyboard/mouse collections); MI_06 puck management | PnP + `SDL_hid_enumerate` |
| Controller via USB | Seen previously only in bootloader mode (`28DE:1005`, `28DE:1007`) during a firmware update. Normal wired mode **not yet tested**. | PnP history |
| Firmware version | Not exposed through SDL (reads `0x0000`) — shown as unavailable | SDL |

## 2. Device access

**Backend:** SDL 3.4.18 official Windows x64 build, HIDAPI driver `SDL_hidapi_steam_triton.c`.
Enabled with `SDL_JOYSTICK_HIDAPI_STEAM=1`; `SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS=1` so input keeps flowing while a game has focus.
SDL 3.2.x does **not** support this controller; ≥ 3.4.x is required.

Measured on real hardware, puck, **Steam running** (`BogiHub.Probe --list`, 17:25):

| Capability | Result |
| --- | --- |
| Buttons | 22 raw buttons + D-pad hat (A/B/X/Y, View, Menu, Steam, QAM, LB/RB, L3/R3, 4 rear paddles, both pad clicks, stick touch ×2, grip touch ×2) |
| Sticks / triggers | 4 stick axes, 2 analog triggers |
| Trackpads | 2, with position and pressure |
| Motion | Gyro + accelerometer, ~249 Hz each |
| Report rate | ~262–264 reports/s |
| Battery | 95 % / 94 %, "on battery" (arrives after the first battery report) |
| Rumble | Supported by the SDL driver; owner reported "works good" for the probe session |

The owner then ran the interactive probe and reported that everything worked. The detailed coverage report
from that session was not saved (window closed with ✕ — fixed since: the probe now autosaves every 2 s and on close).

**Still to record:** which physical paddle is "paddle 1" vs "paddle 2", whether the cursor moved on its own
while using trackpads with Steam open, Steam-closed run, USB-C wired run, Bluetooth run.

## 3. Steam closed vs running

* With Steam **running**, SDL could open the controller and read all inputs (shared HID access works).
* SDL disables the controller's built-in "lizard" mouse/keyboard mode while open and re-sends that every ~3 s;
  when closed, the firmware watchdog restores lizard mode by itself. BogiHub relies on this for its
  "built-in desktop mode" — **time to restore not yet measured**.
* Duplicate input from Steam's desktop layout while BogiHub is in control: **not yet measured**.
  BogiHub detects it heuristically (foreign injected keyboard/mouse events while the controller is in use).

## 4. Virtual controller backend

| Option | Status | Decision |
| --- | --- | --- |
| ViGEmBus 1.22.0 (Nefarius) | Retired upstream (2023) but signed, works on Windows 10/11, MIT client library | **Used** for the Xbox 360 virtual controller. Risk: no future fixes; may break on a future Windows build. Installed separately with admin rights; BogiHub itself never needs admin. |
| VirtualPad (announced successor) | Not confirmed released | Re-check before each release |
| vJoy | DirectInput only | Rejected — Fortnite and most modern games expect XInput |
| Keyboard/mouse via `SendInput` | No driver needed | **Used** as the second output mode. Blocked for elevated (admin) game windows by Windows UIPI. |

Update 29 Oct 2026: the owner installed ViGEmBus 1.22.0 (service running). `BogiHub.Probe --vigem` plugged in a
virtual Xbox 360 pad and read it back through XInput (slot 0: A pressed, RT 200, LX 16000), then verified
neutralise and clean unplug. The full chain (real controller → BogiHub → game) is still untested.

## 5. Recommended stack (confirmed)

C# / .NET 10 · SDL 3.4.18 (zlib) · Nefarius.ViGEm.Client 1.21.256 (MIT) · Avalonia 12.1 (MIT) · xUnit.
Single process; dedicated high-priority input thread; UI never on the input path.

## 6. Open items before Milestone B can be signed off

1. ~~Install ViGEmBus 1.22.0 → confirm the virtual pad~~ Done (XInput check passed).
2. Play one non-Steam game with the default Controller profile.
3. Fortnite: record game version, output mode, transport, Steam state → `docs/compatibility.md`.
4. Steam-closed probe run, USB-C run, Bluetooth run.
