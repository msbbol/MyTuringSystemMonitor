"use strict";

// ---- API --------------------------------------------------------------------------------------

// State-changing calls must carry this header; the service rejects them otherwise (CSRF guard).
const CONTROL_HEADER = "X-Turing-Monitor";

async function api(path, { method = "GET", body } = {}) {
  const init = { method, headers: {} };
  if (method !== "GET") init.headers[CONTROL_HEADER] = "1";
  if (body !== undefined) {
    init.headers["Content-Type"] = "application/json";
    init.body = JSON.stringify(body);
  }

  const res = await fetch(`/api${path}`, init);
  const type = res.headers.get("content-type") || "";
  const data = res.status !== 204 && type.includes("json") ? await res.json() : null;
  if (!res.ok) {
    const error = new Error(`${method} ${path} failed (${res.status})`);
    error.status = res.status;
    error.data = data;
    throw error;
  }
  return data;
}

// ---- helpers ----------------------------------------------------------------------------------

const $ = (id) => document.getElementById(id);
const form = $("settings");

const STATE_LABELS = {
  running: "Running",
  starting: "Starting",
  reconnecting: "Reconnecting",
  paused: "Paused",
  faulted: "Waiting for display",
  stopped: "Stopped",
  offline: "Service offline",
};

const ORIENTATION_LABELS = {
  Portrait: "Portrait",
  ReversePortrait: "Reverse portrait",
  Landscape: "Landscape",
  ReverseLandscape: "Reverse landscape",
};

const SENSOR_LABELS = { Lhm: "LibreHardwareMonitor", PerfCounter: "Performance counters", Fake: "Demo data" };

function timeAgo(iso) {
  if (!iso) return "—";
  const seconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
  if (seconds < 5) return "just now";
  if (seconds < 60) return `${seconds} s ago`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;
  return new Date(iso).toLocaleTimeString();
}

function alignOrientation(orientation, landscape) {
  const reversed = orientation.startsWith("Reverse");
  return landscape ? (reversed ? "ReverseLandscape" : "Landscape") : (reversed ? "ReversePortrait" : "Portrait");
}

let toastTimer;
function toast(message, bad = false) {
  const el = $("toast");
  el.textContent = message;
  el.classList.toggle("bad", bad);
  el.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.hidden = true; }, 4000);
}

// ---- status & live frame ----------------------------------------------------------------------

let lastStatus = null;
let lastFrameCount = -1;

async function refreshStatus() {
  try {
    const status = await api("/status");
    $("offline").hidden = true;
    renderStatus(status);
  } catch {
    $("offline").hidden = false;
    setState("offline");
    $("service-meta").textContent = "Service unreachable";
  }
}

function setState(state) {
  const pill = $("state-pill");
  pill.dataset.state = state;
  $("state-text").textContent = STATE_LABELS[state] || state;
}

function renderStatus({ monitor: m, service: s }) {
  lastStatus = m;
  setState(m.state);

  $("service-meta").textContent =
    `v${s.version} · ${s.runningAsService ? "Windows service" : "console"} · localhost:${s.webPort}`;

  $("f-theme").textContent = m.theme || "—";
  $("f-orientation").textContent = ORIENTATION_LABELS[m.orientation] || "—";
  $("f-resolution").textContent = m.width ? `${m.width} × ${m.height}` : "—";
  $("f-port").textContent = m.displayProvider === "Null" ? "none (dry run)"
    : m.displayProvider === "Esp32Wifi" ? (m.port ? `Wi-Fi ${m.port}` : "Wi-Fi (searching…)")
    : (m.port || "—");
  $("f-sensors").textContent = SENSOR_LABELS[m.sensorProvider] || m.sensorProvider;
  $("f-frames").textContent = m.framesShown.toLocaleString();
  $("f-last").textContent = timeAgo(m.lastFrameAt);
  $("f-refresh").textContent = `${m.refreshIntervalMs} ms`;

  const error = $("last-error");
  error.hidden = !(m.state === "faulted" && m.lastError);
  error.textContent = m.lastError ? `Can't open the display: ${m.lastError} Retrying automatically.` : "";

  const pause = $("btn-pause");
  pause.textContent = m.state === "paused" || m.state === "stopped" ? "Resume" : "Pause";

  if (m.width && m.height)
    $("screen").dataset.orientation = m.width > m.height ? "landscape" : "portrait";

  if (m.framesShown !== lastFrameCount) {
    lastFrameCount = m.framesShown;
    if (m.framesShown > 0) {
      const img = $("frame");
      img.src = `/api/frame?n=${m.framesShown}`;
      img.hidden = false;
      $("frame-empty").hidden = true;
    }
  }

  markActiveTheme(m.theme);
}

$("btn-pause").addEventListener("click", async (event) => {
  const button = event.currentTarget;
  const resume = button.textContent === "Resume";
  button.disabled = true;
  try {
    await api(`/control/${resume ? "resume" : "pause"}`, { method: "POST" });
    toast(resume ? "Display resumed" : "Display paused");
  } catch {
    toast("Couldn't reach the service", true);
  } finally {
    button.disabled = false;
    refreshStatus();
  }
});

$("btn-restart").addEventListener("click", async (event) => {
  const button = event.currentTarget;
  button.disabled = true;
  try {
    await api("/control/restart", { method: "POST" });
    toast("Monitor restarted");
  } catch {
    toast("Couldn't reach the service", true);
  } finally {
    button.disabled = false;
    refreshStatus();
  }
});

// ---- settings ---------------------------------------------------------------------------------

let savedConfig = null;
let selectedTheme = null;

function fillForm(config) {
  ensurePortOption(config.comPort);
  for (const name of ["comPort", "revision", "orientation", "displayProvider", "sensorProvider"])
    form.elements[name].value = config[name];
  form.elements.wifiHost.value = config.wifiHost || "AUTO";
  form.elements.wifiRotation.value = String(config.wifiRotation || 0);
  updateProviderFields();
  form.elements.brightness.value = config.brightness;
  form.elements.refreshIntervalMs.value = config.refreshIntervalMs;
  form.elements.webPort.value = config.webPort;
  selectedTheme = config.theme;
  updateBrightnessLabel();
  markSelectedTheme();
}

function readForm() {
  const el = form.elements;
  return {
    ...savedConfig,
    comPort: el.comPort.value,
    revision: el.revision.value,
    orientation: el.orientation.value,
    brightness: Number(el.brightness.value),
    displayProvider: el.displayProvider.value,
    wifiHost: el.wifiHost.value.trim() || "AUTO",
    wifiRotation: Number(el.wifiRotation.value),
    sensorProvider: el.sensorProvider.value,
    refreshIntervalMs: Number(el.refreshIntervalMs.value),
    webPort: Number(el.webPort.value),
    theme: selectedTheme,
    resetOnStartup: false, // never: resetting re-enumerates the device on another COM port
  };
}

function isDirty() {
  if (!savedConfig) return false;
  const current = readForm();
  return Object.keys(current).some((key) => String(current[key]) !== String(savedConfig[key]));
}

function updateDirty() {
  $("savebar").hidden = !isDirty();
}

function updateBrightnessLabel() {
  $("brightness-out").textContent = `${form.elements.brightness.value}%`;
}

function clearFieldErrors() {
  document.querySelectorAll(".field-error").forEach((el) => { el.textContent = ""; });
}

// Only show the settings that apply to the chosen output (COM port for USB, address for Wi-Fi).
function updateProviderFields() {
  const provider = form.elements.displayProvider.value;
  for (const field of form.querySelectorAll("[data-provider]"))
    field.hidden = field.dataset.provider !== provider;
}

form.addEventListener("input", (event) => {
  if (event.target.name === "brightness") updateBrightnessLabel();
  if (event.target.name === "displayProvider") updateProviderFields();
  updateDirty();
});
form.addEventListener("change", updateDirty);
form.addEventListener("submit", (event) => event.preventDefault());

async function loadConfig() {
  savedConfig = await api("/config");
  fillForm(savedConfig);
  updateDirty();
}

$("btn-discard").addEventListener("click", () => {
  clearFieldErrors();
  fillForm(savedConfig);
  updateDirty();
});

$("btn-save").addEventListener("click", async (event) => {
  const button = event.currentTarget;
  const config = readForm();
  clearFieldErrors();
  button.disabled = true;
  $("savebar-text").textContent = "Applying…";
  try {
    const result = await api("/config", { method: "PUT", body: config });
    savedConfig = config;
    updateDirty();
    toast(result && result.restartServiceRequired
      ? "Saved. Restart the service to use the new control panel port."
      : "Settings applied");
    refreshStatus();
  } catch (error) {
    const errors = error.data && error.data.errors;
    if (errors) {
      for (const [field, messages] of Object.entries(errors)) {
        const target = document.querySelector(`.field-error[data-for="${field}"]`);
        if (target) target.textContent = messages.join(" ");
      }
      toast("Please fix the highlighted settings", true);
    } else {
      toast("Couldn't save — is the service running?", true);
    }
  } finally {
    button.disabled = false;
    $("savebar-text").textContent = "Unsaved changes";
  }
});

// ---- COM ports --------------------------------------------------------------------------------

function ensurePortOption(value) {
  const select = form.elements.comPort;
  if (value && ![...select.options].some((o) => o.value === value)) {
    select.add(new Option(`${value} (not found)`, value));
  }
}

async function loadPorts() {
  const select = form.elements.comPort;
  const current = select.value || (savedConfig && savedConfig.comPort);
  let ports = [];
  try { ports = await api("/ports"); } catch { /* keep what we have */ }

  select.replaceChildren(new Option("AUTO (only serial port present)", "AUTO"));
  for (const port of ports) select.add(new Option(port, port));
  ensurePortOption(current);
  if (current) select.value = current;
}

$("btn-ports").addEventListener("click", async () => {
  await loadPorts();
  toast("Ports rescanned");
  updateDirty();
});

// ---- themes -----------------------------------------------------------------------------------

let themes = [];
let themeFilter = "all";

async function loadThemes() {
  themes = await api("/themes");
  renderThemes();
}

function renderThemes() {
  const grid = $("themes");
  const query = $("theme-search").value.trim().toLowerCase();
  const visible = themes.filter((t) =>
    (themeFilter === "all" || t.orientation === themeFilter) &&
    (!query || t.name.toLowerCase().includes(query)));

  $("theme-count").textContent = `${visible.length}/${themes.length}`;

  if (visible.length === 0) {
    grid.replaceChildren(Object.assign(document.createElement("p"), { className: "empty", textContent: "No themes match." }));
    return;
  }

  const cards = visible.map((theme) => {
    const card = document.createElement("button");
    card.type = "button";
    card.className = "theme";
    card.setAttribute("role", "option");
    card.dataset.name = theme.name;
    card.dataset.orientation = theme.orientation || "";

    const thumb = document.createElement("div");
    thumb.className = "thumb";
    const img = document.createElement("img");
    img.loading = "lazy";
    img.alt = "";
    img.addEventListener("load", () => img.classList.add("loaded"));
    img.src = `/api/themes/${encodeURIComponent(theme.name)}/preview`;
    thumb.append(img);

    const name = document.createElement("span");
    name.className = "theme-name";
    name.textContent = theme.name;
    name.title = theme.name;

    const meta = document.createElement("span");
    meta.className = "theme-meta";
    if (theme.orientation) {
      const badge = document.createElement("span");
      badge.className = "badge";
      badge.textContent = theme.orientation;
      meta.append(badge);
    }
    const active = document.createElement("span");
    active.className = "badge active";
    active.textContent = "on screen";
    active.hidden = true;
    meta.append(active);

    card.append(thumb, name, meta);
    card.addEventListener("click", () => selectTheme(theme));
    return card;
  });

  grid.replaceChildren(...cards);
  markSelectedTheme();
  markActiveTheme(lastStatus && lastStatus.theme);
}

function selectTheme(theme) {
  selectedTheme = theme.name;
  // Match the dropdown to the axis the theme was drawn for, keeping the "reverse" choice.
  if (theme.orientation) {
    const orientation = form.elements.orientation;
    orientation.value = alignOrientation(orientation.value, theme.orientation === "landscape");
  }
  markSelectedTheme();
  updateDirty();
}

function markSelectedTheme() {
  document.querySelectorAll(".theme").forEach((card) => {
    card.setAttribute("aria-selected", String(card.dataset.name === selectedTheme));
  });
}

function markActiveTheme(name) {
  document.querySelectorAll(".theme").forEach((card) => {
    const badge = card.querySelector(".badge.active");
    if (badge) badge.hidden = card.dataset.name !== name;
  });
}

document.querySelectorAll(".segmented button").forEach((button) => {
  button.addEventListener("click", () => {
    themeFilter = button.dataset.filter;
    document.querySelectorAll(".segmented button").forEach((b) => b.setAttribute("aria-checked", String(b === button)));
    renderThemes();
  });
});

$("theme-search").addEventListener("input", renderThemes);

// ---- sensors ----------------------------------------------------------------------------------

const SENSOR_UNITS = {
  load: "%", temp: " °C", power: " W", clock: " MHz", mem: " MB",
  used: " GB", free: " GB", total: " GB", uptime: " h",
  second: " s", minute: " min", hour12: " h", cpu: "%",
};

function formatSensor(key, value) {
  if (value === null || value === undefined) return null;
  // Throughput keys (net.down, disk.1.read, net.1.up, …) are bytes per second.
  if (key.startsWith("net.") || /\.(read|write|down|up)(\.peak)?$/.test(key)) return `${humanBytes(value)}/s`;
  // Video memory sizes are in MB, unlike RAM/disk sizes (GB).
  const unit = key === "gpu.mem.total" ? " MB" : (SENSOR_UNITS[key.split(".").pop()] || "");
  const digits = Math.abs(value) >= 100 ? 0 : 1;
  return `${value.toFixed(digits)}${unit}`;
}

function humanBytes(bytes) {
  if (bytes >= 1e9) return `${(bytes / 1e9).toFixed(1)} GB`;
  if (bytes >= 1e6) return `${(bytes / 1e6).toFixed(1)} MB`;
  if (bytes >= 1e3) return `${(bytes / 1e3).toFixed(0)} KB`;
  return `${bytes.toFixed(0)} B`;
}

async function refreshSensors() {
  let data;
  try { data = await api("/sensors"); } catch { return; }
  const container = $("sensors");
  if (!data) {
    container.replaceChildren(Object.assign(document.createElement("p"), {
      className: "empty", textContent: "No readings — the monitor isn't running.",
    }));
    return;
  }

  // Group "cpu.load" / "cpu.temp" … under "cpu"; text readings (names) go with their group.
  const groups = new Map();
  const add = (key, text, missing) => {
    const group = key.split(".")[0];
    if (!groups.has(group)) groups.set(group, []);
    groups.get(group).push({ key: key.slice(group.length + 1), text, missing });
  };
  for (const [key, value] of Object.entries(data.texts)) add(key, value ?? "—", value == null);
  for (const [key, value] of Object.entries(data.values)) {
    const text = formatSensor(key, value);
    add(key, text ?? "not available", text === null);
  }

  const sections = [...groups.entries()].map(([name, rows]) => {
    const section = document.createElement("section");
    section.className = "sensor-group";
    const title = document.createElement("h3");
    title.textContent = name;
    const list = document.createElement("dl");
    for (const row of rows) {
      const dt = document.createElement("dt");
      dt.textContent = row.key;
      const dd = document.createElement("dd");
      dd.textContent = row.text;
      if (row.missing) dd.className = "missing";
      list.append(dt, dd);
    }
    section.append(title, list);
    return section;
  });
  container.replaceChildren(...sections);
}

// ---- activity log -----------------------------------------------------------------------------

let lastLogSeq = 0;
const MAX_LOG_LINES = 300;

async function refreshLogs() {
  let entries;
  try { entries = await api(`/logs?after=${lastLogSeq}`); } catch { return; }
  if (!entries || entries.length === 0) return;

  const list = $("log");
  const nearBottom = list.scrollHeight - list.scrollTop - list.clientHeight < 40;

  for (const entry of entries) {
    lastLogSeq = Math.max(lastLogSeq, entry.seq);
    const li = document.createElement("li");
    const time = document.createElement("time");
    time.dateTime = entry.time;
    time.textContent = new Date(entry.time).toLocaleTimeString();
    const text = document.createElement("span");
    text.textContent = entry.message;
    li.append(time, text);
    list.append(li);
  }
  while (list.children.length > MAX_LOG_LINES) list.firstElementChild.remove();
  if (nearBottom) list.scrollTop = list.scrollHeight;
}

// ---- boot -------------------------------------------------------------------------------------

(async function start() {
  await refreshStatus();
  try {
    await loadConfig();
    await loadPorts();
    form.elements.comPort.value = savedConfig.comPort;
    updateDirty();
  } catch { /* offline banner already shown */ }
  loadThemes().catch(() => {});
  refreshLogs();
  refreshSensors();

  setInterval(refreshStatus, 2000);
  setInterval(refreshSensors, 2000);
  setInterval(refreshLogs, 3000);
})();
