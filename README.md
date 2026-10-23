# MyTuringSystemMonitor

A C#/.NET system monitor for the **Turing / TURZX 3.5" USB-serial smart display**
(320×480, RevisionA protocol). It reads hardware sensors and draws a live, theme-driven
layout to the screen. The serial wire protocol is handled by the MIT-licensed
[`TuringSmartScreenLib`](https://github.com/usausa/turing-smart-screen) — this project is
everything above it: sensors, theming, config, and the render loop.

> ⚠️ **Do not enable `resetOnStartup`.** Resetting the device on startup makes it disconnect and
> re-enumerate on USB, which can change its COM port number — after which every write silently
> fails against the old port. This was the original *"the screen doesn't work"* root cause.
> The default is `false`; keep it there unless you know you need it. See
> [`DisplayDeviceOptions`](src/MyTuringSystemMonitor/Hardware/DisplayOptions.cs) and
> [`TuringDisplayDevice`](src/MyTuringSystemMonitor/Hardware/TuringDisplayDevice.cs).

## Requirements

- Windows (the app uses GDI+ / `System.Drawing`, which is Windows-only)
- .NET 8 SDK or newer
- The 3.5" display on a COM port (default `COM4`)
- **Administrator** only if you want the full LibreHardwareMonitor sensor set (temps, power,
  clocks, GPU). CPU/RAM work without admin via the performance-counter provider.

## Build & test

```bash
dotnet restore
dotnet build
dotnet test
```

Package versions are pinned to what `dotnet restore` resolved (i.e. what `dotnet add package`
produces). Bump them with `dotnet add package <name>` rather than editing the `.csproj` by hand.

## Install as a Windows service (recommended)

From an **elevated** PowerShell in the repo:

```powershell
powershell -ExecutionPolicy Bypass -File install\install.ps1
```

This builds and installs three things:

- **Windows service** `MyTuringSystemMonitor` (LocalSystem, starts with Windows, restarts itself
  if it crashes). It drives the screen — no console window, no need to stay signed in — and, running
  as LocalSystem, gives LibreHardwareMonitor the rights it needs for temperatures and fans. If the
  screen isn't on USB yet when Windows starts, it keeps retrying every 10 s.
- **Web control panel** at <http://localhost:5321/>, served by the service: live view of what's on
  the screen, pause/resume/restart, all settings, a theme gallery with previews, and an activity
  log. It only listens on localhost; writes require a custom header and a loopback `Host`, so other
  websites open in your browser can't drive it.
- **Tray icon** for every user at sign-in: its colour shows the state (teal running, amber
  starting/reconnecting, grey paused, red display missing / service down); double-click opens the
  control panel; the menu can pause/resume or restart the monitor and start/stop the service
  (asks for UAC).

Re-run the script to upgrade — settings are kept. `-SelfContained` bundles the .NET runtime.
Remove with `install\uninstall.ps1` (add `-RemoveData` to delete settings too).

A Windows service can't show UI, which is why the tray icon is a separate small app that talks to
the service over the same local API as the web page.

## Run from source (development)

```bash
dotnet run --project src/MyTuringSystemMonitor
```

Or run the service host in a console, with the web panel, against a throw-away data folder:

```powershell
$env:TURING_MONITOR_DATA = "$env:TEMP\turing-dev"; dotnet run --project src\MyTuringSystemMonitor.Service
```

Don't run the console monitor while the service is running — both would try to open the COM port.

If the display drops out while running — typically when the laptop sleeps and the USB port loses
power — the monitor logs it once and reconnects on its own (retrying after 2, 4, 8… up to 30 s),
re-applying orientation and brightness. If Windows brings the device back on a different COM
number and it's the only serial port present, the monitor follows it there. Reconnects never
reset the device.

Config and themes live in a machine-wide folder — `%ProgramData%\MyTuringSystemMonitor\`
(`config.json` and `themes\`) — seeded from the files bundled with the app on first run, so the
service, tray, console runner and configurator all use the same settings. Settings from older
versions (in `%LOCALAPPDATA%\MyTuringSystemMonitor\`) are copied over once. Set
`TURING_MONITOR_DATA` to use a different folder. Press **Ctrl+C** to stop — the screen is blanked
and powered off on the way out.

## Configurator (GUI)

A WinForms configuration app (like the mathoudebine `configure.py`) edits the settings, previews
the selected theme live, and launches the monitor:

```bash
dotnet run --project src/MyTuringSystemMonitor.Config
```

It lets you pick the screen model, COM port, orientation, brightness, theme, and monitoring
source, shows a live preview of the theme on the left, and has **Save settings**, **Save and run**
(restarts the service with the new settings when it's running, otherwise launches the console
runner), **Open themes folder**, and **Edit theme**. With the service installed, the web control
panel covers all of this and more.

### Headless dry run (no hardware needed)

Set `"displayProvider": "Null"` and `"sensorProvider": "Fake"` in `config.json` to run the whole
pipeline — config, sensors, theme, render loop — without opening a serial port or needing admin.
Useful for sanity-checking a theme before plugging in the display.

### Diagnostic modes

```bash
# Render the configured theme with sample data to theme-preview.png and exit (no hardware).
dotnet run --project src/MyTuringSystemMonitor -- preview

# Push one calibration pattern to the screen and save calibration.png as the reference.
# The panel should match the PNG 1:1 (round circle, straight grid, corner labels in corners).
dotnet run --project src/MyTuringSystemMonitor -- calibrate
```

Use `preview` to tune a theme's layout to the 320×480 screen; use `calibrate` to confirm the
image maps to the panel with no stretch/shear/rotation.

## Configuration (`config.json`)

| Key                | Default              | Notes                                                             |
|--------------------|----------------------|------------------------------------------------------------------|
| `comPort`          | `"COM4"`             | Serial port, or `"AUTO"` to auto-detect (single-port systems)    |
| `revision`         | `"RevisionA"`        | Screen protocol; `RevisionA` = 3.5"                              |
| `brightness`       | `20`                 | 0–100 %, applied once at startup                                  |
| `orientation`      | `"Portrait"`         | `Portrait` / `ReversePortrait` / `Landscape` / `ReverseLandscape`. A theme that declares an axis overrides this — see [Themes](#themes) |
| `resetOnStartup`   | `false`              | **Leave false** (see warning above)                              |
| `refreshIntervalMs`| `1000`               | Frame interval                                                    |
| `sensorProvider`   | `"Lhm"`              | `Lhm` (full, needs admin) · `PerfCounter` (CPU/RAM) · `Fake`     |
| `displayProvider`  | `"Turing"`           | `Turing` (real screen) · `Null` (headless dry run)               |
| `theme`            | `"3.5inchTheme2"`    | Theme **name** (JSON file name or YAML folder name under the themes dir) |
| `webPort`          | `5321`               | Port of the localhost web control panel (service restart needed) |

## Themes

Two theme formats are supported, both discovered by name from the themes folder:

- **Native JSON** (e.g. [`themes/default.json`](src/MyTuringSystemMonitor/themes/default.json)) —
  a solid or vertical-gradient background (`background` + optional `backgroundGradientTo`) plus
  widgets: `text` (static label, bound sensor value, or `label + value` with a `format`, `scale`,
  `unit`, `anchor`, `humanize`, or a `textSensor`), `datetime` (a clock/date via a .NET format),
  `gauge` (horizontal bar with optional `cornerRadius`), `radial` (circular disc/ring gauge),
  `sparkline` (rolling `line`/`bars` history), `panel` (rounded card), and `clock` (analogue clock
  face). Themes built from custom mockups ship as examples: **`DeskDisplay`**, **`CommandCentre`**
  (analogue clock), **`SystemPulse`** (portrait radial dashboard), and the "Obsidian Command"
  telemetry dashboard in both **`SystemPulsePortrait`** and **`SystemPulseLandscape`** (big CPU
  panel with a live line sparkline, plus GPU/memory/network/storage/thermals tiles). The landscape
  one is 480×320.

### Orientation follows the theme

A theme declares the axis it was drawn for (`"orientation": "portrait" | "landscape"` in JSON,
`DISPLAY_ORIENTATION` in a mathoudebine `theme.yaml`; if neither is present it's inferred from the
background image's aspect). **Picking a theme rotates the screen to match** — no need to change the
orientation setting by hand. Your reverse-variant preference is kept, so `ReversePortrait` + a
landscape theme becomes `ReverseLandscape`. The configurator's Orientation dropdown updates as soon
as you select a theme, and `orientation` in `config.json` is the fallback for themes that don't
declare an axis.
- **Stock-app themes (imported)** — the 35 themes from the bundled Turing/USBMonitor desktop app
  (`New themes/config/*.data`) are imported as folder themes (`<Name>/theme.json` +
  `background.png`): 9 portrait and 26 landscape, with their artwork, bar gauges, ring gauges and
  live values wired to this app's sensors. Re-run the import (e.g. after adding more `.data`
  files) with:

  ```bash
  dotnet run --project tools/VendorThemeImporter
  ```

  Those `.data` files are BinaryFormatter (`usb_hid.ThemeConfig`) payloads; the importer reads them
  with `System.Formats.Nrbf`, flattens the layered background, and maps the vendor's metric names
  (`CpuLoad`, `GpuTemp`, `RamValue`, `InternetDown`, …) onto `SensorKeys`. Metrics this app has no
  source for (fan RPM, disk temperature, audio volume, weather) are skipped.

- **mathoudebine YAML** — folders containing a `theme.yaml`, the same format as
  [turing-smart-screen-python](https://github.com/mathoudebine/turing-smart-screen-python).
  **All 31 of that repo's 3.5" portrait (320×480) themes are bundled** — e.g. `3.5inchTheme2`,
  `Terminal`, `Cyberpunk`, `bash-dark-green`, and the whole `Simple*` family — with their
  backgrounds and fonts. Rendered elements: full-screen background image, `static_text`, per-stat
  `TEXT`, `GRAPH` (horizontal bar), `RADIAL` (circular disc/ring gauge), and `DATE` day/hour.
  (Line/history graphs and some exotic fields like load-average aren't drawn.)
  Themes for other screen sizes (5"/8"/landscape) aren't bundled since they don't fit a 320×480
  portrait panel — drop them in manually if you want to experiment.

**Add more themes:** click *Open themes folder* in the configurator (or open
`%ProgramData%\MyTuringSystemMonitor\themes\`) and drop in a theme folder that contains a
`theme.yaml` plus its assets. Fonts referenced by a theme are resolved from the app's `fonts\`
folder (JetBrains Mono ships with the app); unknown fonts fall back to a system font.

`gpu.load` is the busiest graphics adapter, measured like Task Manager from the Windows "GPU Engine"
counters — on laptops with switchable graphics the integrated GPU does most of the work and the
discrete one reads 0 % while asleep; `gpu.temp`/`gpu.power`/`gpu.mem` come from the discrete GPU via
LibreHardwareMonitor.

Sensor keys: `cpu.load`, `cpu.temp`, `cpu.power`, `cpu.clock`, `gpu.load`, `gpu.temp`,
`gpu.power`, `gpu.mem`, `ram.load`, `ram.used`, `ram.free`, `ram.total`, `swap.load`, `swap.used`, `swap.total`, `disk.load`, `disk.used`,
`disk.free`, `disk.total`, `net.down`, `net.up` (B/s — pair with `"humanize": "bytes"` on a text
widget for 7.4M/860K), `proc.top.cpu`, and the clock pseudo-sensors `time.second`/`time.minute`/
`time.hour12` and `sys.uptime`. Text-valued sensors bind via `"textSensor"` (e.g.
`proc.top.name`). See [`SensorKeys`](src/MyTuringSystemMonitor/Sensors/SensorKeys.cs).

## Architecture

Everything above the hardware layer depends on interfaces, so it's testable without a serial
port or real sensors (there are test doubles for both).

```
Program.cs
  └─ MonitorLoop ── reads ISensorProvider, renders IThemeRenderer, pushes IDisplayDevice
       ├─ ISensorProvider   Lhm / PerfCounter / Fake
       ├─ IThemeRenderer     GdiThemeRenderer (Theme + SensorSnapshot -> Bitmap)
       └─ IDisplayDevice     TuringDisplayDevice (serial) / NullDisplayDevice (test/dry-run)
```

- **Hardware** — `IDisplayDevice`. `TuringDisplayDevice` is the *only* type that touches
  `TuringSmartScreenLib`. `NullDisplayDevice` records calls for tests and dry runs.
- **Sensors** — `ISensorProvider` yields a `SensorSnapshot` of plain values keyed by
  `SensorKeys`, so rendering never depends on LibreHardwareMonitor types.
- **Rendering** — `Theme` = widgets; `GdiThemeRenderer` draws them. Value resolution
  (`TextWidget.ResolveText`, `GaugeWidget.ResolveFraction`) is separate from GDI so it's unit-tested.
- **App** — `MonitorLoop` ties it together and handles clean shutdown.

The solution has three projects: **MyTuringSystemMonitor** (console runner + all core logic),
**MyTuringSystemMonitor.Config** (WinForms configurator, references the runner), and
**MyTuringSystemMonitor.Tests**.

## Status & next steps

Done: build + tests green, hardware behind an interface with a test double, live sensor→render→
display loop, JSON + mathoudebine-YAML themes (with the `3.5inchTheme2` theme bundled), a WinForms
configurator with live preview, headless dry-run mode, disk/date sensors, basic `AUTO` port
resolution.

Not yet done (nice-to-haves):

- Tray icon with Exit (WinForms `NotifyIcon`)
- Smarter `AUTO` port detection by VID/PID or USB serial string (`USB35INCHIPS` / `USB35INCHIPSV2`)
- More YAML widget types (radial gauges, line/history graphs, multiple static images)
- GPU-memory sensor coverage across all vendors

**Hardware verification is still required on a real device** — this was developed without access
to the physical screen. Run it against `COM4` and confirm the display updates.
