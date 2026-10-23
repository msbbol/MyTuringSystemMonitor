# ESP32-4848S040 Wi-Fi display firmware

Turns a Guition **ESP32-4848S040** (ESP32-S3 N16R8, 4" 480×480 ST7701 RGB panel, backlight on
GPIO38) into a network screen for MyTuringSystemMonitor (`"displayProvider": "Esp32Wifi"`).

```bash
pio run -t upload      # build + flash over USB-C
pio device monitor     # serial log; type "wifi-reset" to forget the saved Wi-Fi
```

Built against PlatformIO `espressif32@6.13.0` (Arduino core 2.0.17) with
[Arduino_GFX](https://github.com/moononournation/Arduino_GFX) 1.4.9 and
[WiFiManager](https://github.com/tzapu/WiFiManager) 2.0.17. Arduino_GFX 1.5+ needs Arduino core 3.x,
so keep the two pinned together.

Panel wiring and init sequence (`st7701_type9_init_operations`) match Arduino_GFX's
`ESP32_4848S040_86BOX_GUITION` board definition. If your board shows garbage or a shifted image,
that's the first thing to compare against. Touch (GT911) isn't used yet.

The backlight is dimmed with 1 kHz PWM. Its driver goes dark at partial brightness with PWM
of 5 kHz and above (measured on a 4848S040C_I), so don't raise `kBacklightPwmHz` past ~2 kHz.

The wire protocol is documented at the top of [`src/main.cpp`](src/main.cpp); the PC side is
[`WifiDisplayDevice`](../../src/MyTuringSystemMonitor/Hardware/WifiDisplayDevice.cs).
