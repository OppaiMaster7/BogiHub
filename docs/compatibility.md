# Compatibility matrix

Every entry is one of: **Not tested** · **Working in this configuration** · **Unsupported**.
A working result is a record of one configuration on one date, not a permanent guarantee.
BogiHub never claims to work with every game and never claims to be "anti-cheat safe".

## Transports

| Transport | Steam closed | Steam running | Notes |
| --- | --- | --- | --- |
| Puck (wireless) | Not tested | Working in this configuration (input read, 29 Oct 2026) | ~262 reports/s, battery readable |
| USB-C cable | Not tested | Not tested | |
| Bluetooth | Not tested | Not tested | |

## Output paths

| Output | Status | Notes |
| --- | --- | --- |
| Virtual Xbox controller (ViGEmBus 1.22.0) | Working in this configuration (29 Oct 2026) | `BogiHub.Probe --vigem`: XInput slot 0 saw A, RT and left stick; neutralise and unplug verified. Not yet driven by the real controller in a game. |
| Keyboard & mouse (SendInput) | Not tested | Fails for games running as administrator (Windows UIPI) |
| Rumble pass-through | Not tested | Needs a game + ViGEmBus |
| On-screen notifications | Working on the desktop (30 Oct 2026) | `artifacts/uitest/notices.ps1`: overlay drawn see-through, clicks pass through, focus not taken, hidden afterwards. Not yet seen over a game. Exclusive-fullscreen games draw over every window, so notices can't show there (borderless/windowed only). Battery and connect/disconnect notices not yet triggered by the real controller. |

## Games

| Game | Launcher | Version | Output | Transport | Steam | Result | Date |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Fortnite | Epic Games Launcher | — | Xbox controller | — | — | Not tested | — |

Fortnite uses Easy Anti-Cheat with kernel-level protection. BogiHub only creates standard input devices
through documented Windows interfaces; it does not touch game memory or bypass anti-cheat. Whether a game
accepts virtual controllers is the game's decision and can change with any update.
