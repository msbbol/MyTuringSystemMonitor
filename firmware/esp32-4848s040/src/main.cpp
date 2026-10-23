// Wi-Fi display firmware for the Guition ESP32-4848S040 (ESP32-S3 + 4" 480x480 ST7701 RGB panel).
//
// The PC (MyTuringSystemMonitor, display provider "Esp32Wifi") renders the theme and streams
// changed rectangles of the frame here over TCP. This side only blits pixels — no theming, no
// sensors. Protocol (all integers little-endian), TCP port 47800:
//
//   on connect, device -> PC:  "TSMW" u8 version  u16 width  u16 height
//   PC -> device, 1-byte opcode then payload:
//     0x01 RECT        u16 x  u16 y  u16 w  u16 h  then w*h RGB565 pixels (u16 each)
//     0x02 BRIGHTNESS  u8 percent (0-100)
//     0x03 SCREEN_ON
//     0x04 SCREEN_OFF
//     0x05 FILL        u16 RGB565 colour
//     0x06 SYNC        device answers one byte 0x06 once everything before it is on screen
//     0x07 ROTATION    u8 quarter turns clockwise (0-3)
//
// Discovery: a UDP datagram "TSMW?" to port 47801 (broadcast) is answered with
// "TSMW! <version> <hostname> <width> <height>". The device also registers <hostname>.local via mDNS.
//
// Wi-Fi credentials are set through a captive portal (WiFiManager): on first boot, or when the
// saved network can't be joined, the board opens the access point "TuringESP32-Setup".
// Send "wifi-reset" over the serial console to forget the saved network.

#include <Arduino.h>
#include <ESPmDNS.h>
#include <WiFi.h>
#include <WiFiManager.h>
#include <WiFiUdp.h>
#include <Arduino_GFX_Library.h>

namespace {

constexpr uint16_t kTcpPort = 47800;
constexpr uint16_t kDiscoveryPort = 47801;
constexpr uint8_t kProtocolVersion = 1;

constexpr int kPanelWidth = 480;
constexpr int kPanelHeight = 480;
constexpr int kBacklightPin = 38;
constexpr int kBacklightChannel = 0;
// The backlight driver only dims at low PWM frequencies: measured on a 4848S040C_I, 2 kHz and
// below work, 5 kHz and up turn the backlight off at anything under 100 %.
constexpr uint32_t kBacklightPwmHz = 1000;

// A stalled transfer (PC asleep, Wi-Fi gone) is abandoned after this long mid-command...
constexpr uint32_t kReadTimeoutMs = 5000;
// ...and a connection that sends nothing at all is dropped after this long. The PC pushes at
// least one frame per refresh interval (default 1 s), so this only trips on a dead peer.
constexpr uint32_t kIdleTimeoutMs = 30000;

// Pixels are read from the socket in strips of at most this many pixels, then blitted.
constexpr size_t kChunkPixels = kPanelWidth * 16;

enum Command : uint8_t {
  kCmdRect = 0x01,
  kCmdBrightness = 0x02,
  kCmdScreenOn = 0x03,
  kCmdScreenOff = 0x04,
  kCmdFill = 0x05,
  kCmdSync = 0x06,
  kCmdRotation = 0x07,
};

// ---- Panel wiring (ESP32-4848S040, same as Arduino_GFX's ESP32_4848S040_86BOX_GUITION) -------

Arduino_DataBus *bus = new Arduino_SWSPI(
    GFX_NOT_DEFINED /* DC */, 39 /* CS */, 48 /* SCK */, 47 /* MOSI */, GFX_NOT_DEFINED /* MISO */);

Arduino_ESP32RGBPanel *rgbpanel = new Arduino_ESP32RGBPanel(
    18 /* DE */, 17 /* VSYNC */, 16 /* HSYNC */, 21 /* PCLK */,
    11 /* R0 */, 12 /* R1 */, 13 /* R2 */, 14 /* R3 */, 0 /* R4 */,
    8 /* G0 */, 20 /* G1 */, 3 /* G2 */, 46 /* G3 */, 9 /* G4 */, 10 /* G5 */,
    4 /* B0 */, 5 /* B1 */, 6 /* B2 */, 7 /* B3 */, 15 /* B4 */,
    1 /* hsync_polarity */, 10 /* hsync_front_porch */, 8 /* hsync_pulse_width */, 50 /* hsync_back_porch */,
    1 /* vsync_polarity */, 10 /* vsync_front_porch */, 8 /* vsync_pulse_width */, 20 /* vsync_back_porch */,
    0 /* pclk_active_neg */, 12000000 /* prefer_speed */);

Arduino_RGB_Display *gfx = new Arduino_RGB_Display(
    kPanelWidth, kPanelHeight, rgbpanel, 0 /* rotation */, true /* auto_flush */,
    bus, GFX_NOT_DEFINED /* RST */, st7701_type9_init_operations, sizeof(st7701_type9_init_operations));

// ---- State -------------------------------------------------------------------------------------

WiFiServer server(kTcpPort);
WiFiUDP discovery;
WiFiClient client;
bool clientActive = false;
uint32_t lastRxAt = 0;

char hostname[32];
uint16_t *chunk = nullptr;

uint8_t brightnessPercent = 50;
// Set when the PC turned the screen off on purpose (pause / shutdown): stay dark after it leaves.
bool screenOffByHost = false;
bool wifiWasConnected = false;

// ---- Backlight & status screen -----------------------------------------------------------------

void applyBacklight(bool on) {
  uint32_t duty = on ? (uint32_t)brightnessPercent * 255 / 100 : 0;
  ledcWrite(kBacklightChannel, duty);
}

void drawStatus(const char *title, const char *line1, const char *line2 = nullptr,
                const char *line3 = nullptr) {
  gfx->fillScreen(BLACK);
  gfx->fillRect(0, 0, gfx->width(), 64, 0x0A4F);   // teal header
  gfx->setTextColor(WHITE);
  gfx->setTextSize(3);
  gfx->setCursor(20, 20);
  gfx->print("Turing Wi-Fi display");

  gfx->setTextSize(3);
  gfx->setTextColor(0xFFE0);
  gfx->setCursor(20, 120);
  gfx->print(title);

  gfx->setTextColor(WHITE);
  gfx->setTextSize(2);
  int y = 180;
  for (const char *line : {line1, line2, line3}) {
    if (!line) continue;
    gfx->setCursor(20, y);
    gfx->print(line);
    y += 36;
  }
  applyBacklight(true);
}

void drawWaiting() {
  char ip[48], host[64];
  snprintf(ip, sizeof(ip), "IP:   %s", WiFi.localIP().toString().c_str());
  snprintf(host, sizeof(host), "Host: %s.local", hostname);
  drawStatus("Waiting for PC...", ip, host, "Pick output \"ESP32 Wi-Fi\" in the app");
}

// ---- Socket helpers ----------------------------------------------------------------------------

bool readExact(uint8_t *dst, size_t n) {
  uint32_t lastProgress = millis();
  while (n > 0) {
    int available = client.available();
    if (available > 0) {
      int got = client.read(dst, min((size_t)available, n));
      if (got > 0) {
        dst += got;
        n -= got;
        lastProgress = millis();
        continue;
      }
    }
    if (!client.connected()) return false;
    if (millis() - lastProgress > kReadTimeoutMs) return false;
    delay(1);
  }
  return true;
}

void sendHello() {
  uint8_t hello[9] = {'T', 'S', 'M', 'W', kProtocolVersion,
                      (uint8_t)(kPanelWidth & 0xFF), (uint8_t)(kPanelWidth >> 8),
                      (uint8_t)(kPanelHeight & 0xFF), (uint8_t)(kPanelHeight >> 8)};
  client.write(hello, sizeof(hello));
}

void dropClient(const char *reason) {
  Serial.printf("Client %s: %s\n", client.remoteIP().toString().c_str(), reason);
  client.stop();
  clientActive = false;
  if (screenOffByHost)
    applyBacklight(false);
  else
    drawWaiting();
}

// ---- Commands ----------------------------------------------------------------------------------

bool handleRect() {
  uint16_t header[4];
  if (!readExact((uint8_t *)header, sizeof(header))) return false;
  const int x = header[0], y = header[1], w = header[2], h = header[3];
  if (w == 0 || h == 0 || x + w > gfx->width() || y + h > gfx->height()) {
    Serial.printf("Bad rect %d,%d %dx%d\n", x, y, w, h);
    return false;
  }

  const int rowsPerChunk = max(1, (int)(kChunkPixels / w));
  for (int row = 0; row < h;) {
    const int rows = min(rowsPerChunk, h - row);
    if (!readExact((uint8_t *)chunk, (size_t)w * rows * 2)) return false;
    gfx->draw16bitRGBBitmap(x, y + row, chunk, w, rows);
    row += rows;
  }
  return true;
}

bool handleCommand(uint8_t op) {
  switch (op) {
    case kCmdRect:
      return handleRect();

    case kCmdBrightness: {
      uint8_t percent;
      if (!readExact(&percent, 1)) return false;
      brightnessPercent = min<uint8_t>(percent, 100);
      if (!screenOffByHost) applyBacklight(true);
      return true;
    }

    case kCmdScreenOn:
      screenOffByHost = false;
      applyBacklight(true);
      return true;

    case kCmdScreenOff:
      screenOffByHost = true;
      applyBacklight(false);
      return true;

    case kCmdFill: {
      uint16_t color;
      if (!readExact((uint8_t *)&color, 2)) return false;
      gfx->fillScreen(color);
      return true;
    }

    case kCmdSync: {
      const uint8_t ack = kCmdSync;
      return client.write(&ack, 1) == 1;
    }

    case kCmdRotation: {
      uint8_t quarterTurns;
      if (!readExact(&quarterTurns, 1)) return false;
      gfx->setRotation(quarterTurns & 3);
      return true;
    }

    default:
      Serial.printf("Unknown command 0x%02X\n", op);
      return false;
  }
}

// ---- Network services --------------------------------------------------------------------------

void serviceDiscovery() {
  int size = discovery.parsePacket();
  if (size <= 0) return;

  char request[16] = {0};
  discovery.read(request, sizeof(request) - 1);
  if (strncmp(request, "TSMW?", 5) != 0) return;

  char reply[96];
  int len = snprintf(reply, sizeof(reply), "TSMW! %u %s %d %d", kProtocolVersion, hostname,
                     kPanelWidth, kPanelHeight);
  discovery.beginPacket(discovery.remoteIP(), discovery.remotePort());
  discovery.write((const uint8_t *)reply, len);
  discovery.endPacket();
}

void acceptClient() {
  if (!server.hasClient()) return;

  // One PC at a time; a new connection wins, so a restarted monitor takes over immediately
  // instead of waiting for its old half-open socket to time out.
  WiFiClient incoming = server.available();
  if (clientActive) {
    Serial.println("New client replaces the current one");
    client.stop();
  }
  client = incoming;
  client.setNoDelay(true);
  clientActive = true;
  lastRxAt = millis();
  Serial.printf("Client connected: %s\n", client.remoteIP().toString().c_str());
  sendHello();
}

void serviceClient() {
  if (!clientActive) return;

  if (client.available() == 0) {
    if (!client.connected())
      dropClient("disconnected");
    else if (millis() - lastRxAt > kIdleTimeoutMs)
      dropClient("idle timeout");
    return;
  }

  // Drain whatever is queued, but give discovery and new connections a turn now and then.
  const uint32_t started = millis();
  while (client.available() > 0 && millis() - started < 200) {
    uint8_t op;
    if (!readExact(&op, 1) || !handleCommand(op)) {
      dropClient("connection error");
      return;
    }
    lastRxAt = millis();
  }
}

void watchWifi() {
  const bool connected = WiFi.status() == WL_CONNECTED;
  if (connected == wifiWasConnected) return;
  wifiWasConnected = connected;

  if (!connected) {
    Serial.println("Wi-Fi lost, reconnecting...");
    if (clientActive) {
      client.stop();
      clientActive = false;
    }
    if (!screenOffByHost) drawStatus("Wi-Fi lost", "Reconnecting...");
  } else {
    Serial.printf("Wi-Fi back: %s\n", WiFi.localIP().toString().c_str());
    if (!clientActive && !screenOffByHost) drawWaiting();
  }
}

void serviceSerial() {
  static String line;
  while (Serial.available()) {
    char c = (char)Serial.read();
    if (c == '\r' || c == '\n') {
      line.trim();
      if (line == "wifi-reset") {
        Serial.println("Forgetting Wi-Fi settings and restarting");
        WiFiManager().resetSettings();
        delay(200);
        ESP.restart();
      } else if (line.length() > 0) {
        Serial.println("Commands: wifi-reset");
      }
      line = "";
    } else if (line.length() < 32) {
      line += c;
    }
  }
}

// Joins the network saved by the setup portal. The first attempt after power-on sometimes fails
// with an auth error even with the right password, so try a few times, each from a clean state.
bool joinSavedWifi() {
  wifi_config_t saved = {};
  esp_wifi_get_config(WIFI_IF_STA, &saved);
  const char *ssid = (const char *)saved.sta.ssid;
  if (ssid[0] == 0) return false;   // nothing saved yet

  for (int attempt = 1; attempt <= 3; attempt++) {
    Serial.printf("Joining \"%s\", attempt %d of 3\n", ssid, attempt);
    WiFi.begin();
    const uint32_t started = millis();
    while (millis() - started < 15000) {
      const wl_status_t status = WiFi.status();
      if (status == WL_CONNECTED) return true;
      // The status still holds the previous attempt's failure for a moment after begin().
      if (millis() - started > 3000 && (status == WL_CONNECT_FAILED || status == WL_NO_SSID_AVAIL)) break;
      delay(100);
    }
    WiFi.disconnect();
    delay(1000);
  }
  return false;
}

}  // namespace

void setup() {
  Serial.begin(115200);

  ledcSetup(kBacklightChannel, kBacklightPwmHz, 8);
  ledcAttachPin(kBacklightPin, kBacklightChannel);
  applyBacklight(false);

  if (!gfx->begin()) Serial.println("Display init failed");

  chunk = (uint16_t *)heap_caps_malloc(kChunkPixels * 2, MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
  if (!chunk) chunk = (uint16_t *)malloc(kChunkPixels * 2);

  WiFi.mode(WIFI_STA);
  uint8_t mac[6];
  WiFi.macAddress(mac);
  snprintf(hostname, sizeof(hostname), "turing-esp32-%02x%02x", mac[4], mac[5]);

  drawStatus("Connecting to Wi-Fi...", hostname);

  // Log why a join fails (wrong password, 5 GHz-only network, ...) — the portal alone doesn't say.
  WiFi.onEvent([](arduino_event_id_t, arduino_event_info_t info) {
    Serial.printf("Wi-Fi disconnected, reason %u\n", info.wifi_sta_disconnected.reason);
  }, ARDUINO_EVENT_WIFI_STA_DISCONNECTED);

  WiFi.setHostname(hostname);
  if (!joinSavedWifi()) {
    WiFiManager wm;
    wm.setHostname(hostname);
    wm.setConnectTimeout(20);
    wm.setAPCallback([](WiFiManager *) {
      drawStatus("Wi-Fi setup", "Join the Wi-Fi network", "  TuringESP32-Setup",
                 "then open http://192.168.4.1");
    });
    if (!wm.startConfigPortal("TuringESP32-Setup")) {
      Serial.println("Wi-Fi setup failed, restarting");
      delay(1000);
      ESP.restart();
    }
  }

  WiFi.setSleep(false);   // modem sleep adds 100+ ms of latency per packet
  WiFi.setAutoReconnect(true);
  wifiWasConnected = true;

  if (MDNS.begin(hostname)) MDNS.addService("tsmw", "tcp", kTcpPort);
  server.begin();
  server.setNoDelay(true);
  discovery.begin(kDiscoveryPort);

  Serial.printf("Ready: %s (%s), TCP %u, discovery UDP %u\n", hostname,
                WiFi.localIP().toString().c_str(), kTcpPort, kDiscoveryPort);
  drawWaiting();
}

void loop() {
  watchWifi();
  serviceSerial();
  if (wifiWasConnected) {
    serviceDiscovery();
    acceptClient();
    serviceClient();
  }
  delay(1);
}
