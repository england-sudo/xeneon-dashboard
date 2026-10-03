# XeneonDash

A system-stats dashboard for the Corsair Xeneon Edge 14.5" touchscreen
(2560×720), though it runs fullscreen on any Windows display. Dark theme,
touch-friendly, no iCUE required. Stats come from
[LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).

## Pages

- **Overview** — CPU/GPU temp gauges, load + VRAM bars, RAM, drive temps and
  SMART health, live network up/down, uptime
- **CPU** — package temp, clocks, power, voltage, per-core load bars
- **GPU** — temp, load, VRAM, clocks, fan, power, hotspot (multi-GPU supported)
- **History** — 4-minute bar graphs for the headline metrics
- **System** — fan RPMs, motherboard temps, machine/OS info

Stat rows show the session **MIN · MAX · AVG** underneath each metric.

## Install (recommended)

Run **`XeneonDash-Setup.exe`**:

- Self-contained: bundles the .NET runtime, nothing else to install
- Installs to `C:\Program Files\XeneonDash` with Start Menu shortcuts and a
  proper entry in Settings → Apps for uninstalling
- Optional during setup: a desktop shortcut, and **Start with Windows (as
  administrator)** — registers a sign-in task that runs elevated with no UAC
  prompt, which the app needs for full GPU/drive sensors
- Settings (`%APPDATA%\XeneonDash\settings.json`), CSV sensor logs
  (`Documents\XeneonDash\logs`) and your themes survive uninstall/reinstall

The setup is unsigned, so Windows SmartScreen will warn on first run —
"More info → Run anyway".

For full sensor coverage (CPU package power, GPU power/clocks, fan speeds,
drive SMART health) the app needs to run as administrator. The Start-with-
Windows task handles that automatically; for a manual launch, right-click →
Run as administrator.

## Build (on your Windows PC)

You need the **.NET 8 SDK**: https://dotnet.microsoft.com/download

- **`build.bat`** — quick debug build to
  `XeneonDash\bin\Release\net8.0-windows\` (needs the .NET 8 Desktop Runtime
  on the PC that runs it)
- **`build.bat publish`** — portable single-file exe at `publish\XeneonDash.exe`
- **`build.bat installer`** — self-contained publish + installer (also needs
  [NSIS 3](https://nsis.sourceforge.io/)); produces `XeneonDash-Setup.exe`

## Using it

- **SETUP**: pick which display it fills (auto = secondary display when one
  is connected), pick a theme, toggle always-on-top / start-with-Windows /
  CSV logging, change refresh rate
- **Esc** exits
- `XeneonDash.exe --demo` runs with fake data — handy for checking out the
  UI on a machine without sensors
- If it ever fails to start, the error is written to `XeneonDash-crash.log`
  (next to the exe, or in `%LOCALAPPDATA%\XeneonDash`)

## Layout

Designed for 2560×720 landscape. If Windows display scaling on the Edge is
set above 100%, drop it to 100% in Settings → Display for the sharpest fit,
or use the text-size slider in SETUP.

## Licenses

The app ships with LibreHardwareMonitor (MPL-2.0) and the .NET runtime
(MIT); license texts are in the `Licenses` folder next to the exe.
