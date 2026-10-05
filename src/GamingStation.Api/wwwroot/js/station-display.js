// Read-only station display. The backend owns session state.
//
// Clock: GET /api/health returns serverTimeUtc. The page stores
// serverTimeUtc minus the local time at the middle of that request.
// Later, estimated server time is Date.now() plus that offset, so a TV
// clock that is wrong still counts down toward ExpiryTimeUtc.
// This is not NTP. The offset is refreshed on each REST load.
//
// The countdown may show 00:00 when the local estimate reaches the
// server expiry. The page does not change GAME RUNNING to TIME EXPIRED.
// That change happens only when StationStateChanged says awaitingPayment.

const stationElement = document.getElementById("station");
const stateElement = document.getElementById("state");
const countdownElement = document.getElementById("countdown");
const connectionElement = document.getElementById("connection");

const labels = {
  running: "GAME RUNNING",
  awaitingPayment: "TIME EXPIRED"
};

const connectionLabels = {
  connected: "CONNECTED",
  reconnecting: "RECONNECTING",
  lost: "CONNECTION LOST"
};

let canonicalCode = null;
let stationName = "";
let currentSession = null;
let serverOffsetMs = 0;
let connection = null;
let recoverTimer = 0;
let healthTimer = 0;
let closingForProbe = false;

function requestedCode() {
  const parts = location.pathname.split("/").filter(Boolean);
  const displayIndex = parts.findIndex((part) => part.toLowerCase() === "display");
  if (displayIndex < 0 || displayIndex + 1 >= parts.length) {
    return "";
  }

  return decodeURIComponent(parts[displayIndex + 1]).trim();
}

function serverNow() {
  return Date.now() + serverOffsetMs;
}

function formatCountdown(expiryTimeUtc) {
  const remainingMs = Date.parse(expiryTimeUtc) - serverNow();
  const totalSeconds = remainingMs <= 0 ? 0 : Math.ceil(remainingMs / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return String(minutes).padStart(2, "0") + ":" + String(seconds).padStart(2, "0");
}

function setConnectionStatus(status) {
  document.body.dataset.connection = status;
  connectionElement.textContent = connectionLabels[status] || status;
}

function render() {
  const backendState = currentSession ? currentSession.state : "none";
  document.body.dataset.sessionState = backendState;
  document.body.dataset.sessionId = currentSession ? currentSession.id : "";
  stationElement.textContent = stationName;
  stateElement.textContent = currentSession ? (labels[currentSession.state] || currentSession.state) : "AVAILABLE";

  if (currentSession && currentSession.state === "running") {
    countdownElement.textContent = formatCountdown(currentSession.expiryTimeUtc);
    countdownElement.classList.add("visible");
    return;
  }

  countdownElement.textContent = "";
  countdownElement.classList.remove("visible");
}

function findStation(stations, code) {
  const exact = stations.find((station) => station.stationCode === code);
  if (exact) {
    return exact;
  }

  const lower = code.toLowerCase();
  return stations.find((station) => station.stationCode.toLowerCase() === lower) || null;
}

async function syncClock() {
  const before = Date.now();
  const response = await fetch("/api/health", { cache: "no-store" });
  if (!response.ok) {
    throw new Error("Health request failed.");
  }

  const after = Date.now();
  const health = await response.json();
  serverOffsetMs = Date.parse(health.serverTimeUtc) - ((before + after) / 2);
}

async function loadStation() {
  const response = await fetch("/api/stations", { cache: "no-store" });
  if (!response.ok) {
    throw new Error("Station request failed.");
  }

  const station = findStation(await response.json(), requestedCode());
  if (!station) {
    canonicalCode = null;
    stationName = "";
    currentSession = null;
    document.body.dataset.sessionState = "";
    document.body.dataset.sessionId = "";
    stationElement.textContent = "";
    stateElement.textContent = "STATION NOT FOUND";
    countdownElement.textContent = "";
    countdownElement.classList.remove("visible");
    return false;
  }

  canonicalCode = station.stationCode;
  stationName = station.name || station.stationCode;
  currentSession = station.currentSession || null;
  render();
  return true;
}

function applySnapshot(snapshot) {
  if (!canonicalCode || !snapshot || snapshot.stationCode !== canonicalCode) {
    return;
  }

  if (snapshot.stationName) {
    stationName = snapshot.stationName;
  }
  currentSession = snapshot.currentSession || null;
  render();
}

function startHealthProbe() {
  if (healthTimer !== 0) {
    return;
  }

  healthTimer = window.setInterval(() => {
    probeHealth();
  }, 5000);
}

function stopHealthProbe() {
  if (healthTimer !== 0) {
    window.clearInterval(healthTimer);
    healthTimer = 0;
  }
}

async function probeHealth() {
  if (!connection || closingForProbe || connection.state !== signalR.HubConnectionState.Connected) {
    return;
  }

  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), 2000);
  try {
    const response = await fetch("/api/health", {
      cache: "no-store",
      signal: controller.signal
    });
    if (!response.ok) {
      throw new Error("Health request failed.");
    }
  } catch {
    if (!connection || closingForProbe || connection.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    closingForProbe = true;
    setConnectionStatus("lost");
    try {
      await connection.stop();
    } catch {
      // onclose still schedules the existing recovery flow.
    } finally {
      closingForProbe = false;
    }
  } finally {
    window.clearTimeout(timeout);
  }
}

function scheduleRecover() {
  if (recoverTimer !== 0) {
    return;
  }

  recoverTimer = window.setTimeout(() => {
    recoverTimer = 0;
    recover();
  }, 3000);
}

async function recover() {
  try {
    await syncClock();
    const found = await loadStation();
    if (!found) {
      setConnectionStatus("connected");
      return;
    }

    if (connection.state === signalR.HubConnectionState.Disconnected) {
      await connection.start();
    }

    if (connection.state === signalR.HubConnectionState.Connected) {
      await connection.invoke("JoinStation", canonicalCode);
      setConnectionStatus("connected");
    }
  } catch {
    setConnectionStatus("lost");
    scheduleRecover();
  }
}

function wireConnection() {
  connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/stations")
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (retry) => Math.min(10000, 1000 * (retry.previousRetryCount + 1))
    })
    .build();

  connection.on("StationStateChanged", applySnapshot);
  // The hub is down. Keep the last station state and keep retrying.
  connection.onreconnecting(() => {
    setConnectionStatus("lost");
  });
  connection.onreconnected(() => {
    recover();
  });
  connection.onclose(() => {
    setConnectionStatus("lost");
    scheduleRecover();
  });
}

window.setInterval(() => {
  if (currentSession && currentSession.state === "running") {
    countdownElement.textContent = formatCountdown(currentSession.expiryTimeUtc);
  }
}, 200);

try {
  setConnectionStatus("reconnecting");
  wireConnection();
  startHealthProbe();
  window.addEventListener("pagehide", stopHealthProbe);
  recover();
} catch {
  setConnectionStatus("lost");
}
