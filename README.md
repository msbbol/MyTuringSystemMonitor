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

On first run a `config.json` is written next to the binary. Press **Ctrl+C** to stop — the screen
is blanked and powered off on the way out.

### Headless dry run (no hardware needed)

Set `"displayProvider": "Null"` and `"sensorProvider": "Fake"` in `config.json` to run the whole
pipeline — config, sensors, theme, render loop — without opening a serial port or needing admin.
Useful for sanity-checking a theme before plugging in the display.

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
| `theme`            | `"themes/default.json"` | Path to a theme file; falls back to the built-in theme        |

## Themes

A theme is a background plus a list of widgets, each bound to a sensor by key. See
[`themes/default.json`](src/MyTuringSystemMonitor/themes/default.json). Widget types:

- `text` — a static label, a bound sensor value, or `label + value` (with a `format` string)
- `gauge` — a horizontal bar scaled between `min` and `max`

Sensor keys: `cpu.load`, `cpu.temp`, `cpu.power`, `cpu.clock`, `gpu.load`, `gpu.temp`,
`gpu.power`, `ram.load`, `ram.used`, `ram.total`
(see [`SensorKeys`](src/MyTuringSystemMonitor/Sensors/SensorKeys.cs)).

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

## Status & next steps

Done: build + tests green, hardware behind an interface with a test double, live sensor→render→
display loop, JSON config and JSON themes, headless dry-run mode, basic `AUTO` port resolution.

Not yet done (nice-to-haves from the brief):

- Tray icon with Exit (WinForms `NotifyIcon`)
- Settings window instead of hand-edited JSON
- Smarter `AUTO` port detection by VID/PID or USB serial string (`USB35INCHIPS` / `USB35INCHIPSV2`)
- More widget types (line/bar history graphs, static images)

**Hardware verification is still required on a real device** — this was developed without access
to the physical screen. Run it against `COM4` and confirm the display updates.
