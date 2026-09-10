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

## Run

```bash
dotnet run --project src/MyTuringSystemMonitor
```

Config and themes live in a shared per-user folder — `%LOCALAPPDATA%\MyTuringSystemMonitor\`
(`config.json` and `themes\`) — seeded from the files bundled with the app on first run, so the
console runner and the configurator use the same settings. Press **Ctrl+C** to stop — the screen
is blanked and powered off on the way out.

## Configurator (GUI)

A WinForms configuration app (like the mathoudebine `configure.py`) edits the settings, previews
the selected theme live, and launches the monitor:

```bash
dotnet run --project src/MyTuringSystemMonitor.Config
```

It lets you pick the screen model, COM port, orientation, brightness, theme, and monitoring
source, shows a live preview of the theme on the left, and has **Save settings**, **Save and run**
(launches the console runner), **Open themes folder**, and **Edit theme**.

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
| `orientation`      | `"Portrait"`         | `Portrait` / `ReversePortrait` / `Landscape` / `ReverseLandscape`|
| `resetOnStartup`   | `false`              | **Leave false** (see warning above)                              |
| `refreshIntervalMs`| `1000`               | Frame interval                                                    |
| `sensorProvider`   | `"Lhm"`              | `Lhm` (full, needs admin) · `PerfCounter` (CPU/RAM) · `Fake`     |
| `displayProvider`  | `"Turing"`           | `Turing` (real screen) · `Null` (headless dry run)               |
| `theme`            | `"3.5inchTheme2"`    | Theme **name** (JSON file name or YAML folder name under the themes dir) |

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
  one is 480×320: set `orientation` to `Landscape` and the canvas auto-swaps (the physical panel
  reports 480×320 in that orientation).
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
`%LOCALAPPDATA%\MyTuringSystemMonitor\themes\`) and drop in a theme folder that contains a
`theme.yaml` plus its assets. Fonts referenced by a theme are resolved from the app's `fonts\`
folder (JetBrains Mono ships with the app); unknown fonts fall back to a system font.

Sensor keys: `cpu.load`, `cpu.temp`, `cpu.power`, `cpu.clock`, `gpu.load`, `gpu.temp`,
`gpu.power`, `gpu.mem`, `ram.load`, `ram.used`, `ram.total`, `disk.load`, `disk.used`,
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
