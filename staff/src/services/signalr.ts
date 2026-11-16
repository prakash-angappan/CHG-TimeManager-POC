import * as signalR from "@microsoft/signalr";
import type { ConnectionStatus, StationSnapshot } from "../types/station";

export interface StationHub {
  setStations: (stationCodes: string[]) => void;
  setBeforeJoin: (reload: () => Promise<void>) => void;
  start: () => Promise<void>;
  stop: () => Promise<void>;
}

export function createStationHub(
  onSnapshot: (snapshot: StationSnapshot) => void,
  onStatus: (status: ConnectionStatus) => void,
): StationHub {
  let stationCodes: string[] = [];
  let beforeJoin: () => Promise<void> = async () => {};
  let recoverTimer = 0;
  let healthTimer = 0;
  let stopped = false;
  let closingForProbe = false;
  let probeOutage = false;

  const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/stations")
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (retry) => Math.min(10_000, 1_000 * (retry.previousRetryCount + 1)),
    })
    .build();

  connection.on("StationStateChanged", onSnapshot);
  connection.onreconnecting(() => onStatus("reconnecting"));
  connection.onreconnected(() => {
    void recover();
  });
  connection.onclose(() => {
    if (stopped) {
      return;
    }

    if (!closingForProbe) {
      onStatus("lost");
    }

    scheduleRecover();
  });

  async function joinAll(): Promise<void> {
    for (const stationCode of stationCodes) {
      await connection.invoke("JoinStation", stationCode);
    }
  }

  async function recover(): Promise<void> {
    try {
      await beforeJoin();
      if (connection.state === signalR.HubConnectionState.Disconnected) {
        await connection.start();
      }

      if (connection.state === signalR.HubConnectionState.Connected) {
        await joinAll();
        probeOutage = false;
        onStatus("connected");
      }
    } catch {
      if (!stopped) {
        onStatus(probeOutage ? "reconnecting" : "lost");
        scheduleRecover();
      }
    }
  }

  function startHealthProbe(): void {
    if (healthTimer !== 0) {
      return;
    }

    healthTimer = window.setInterval(() => {
      void probeHealth();
    }, 5_000);
  }

  function stopHealthProbe(): void {
    if (healthTimer !== 0) {
      window.clearInterval(healthTimer);
      healthTimer = 0;
    }
  }

  async function probeHealth(): Promise<void> {
    if (stopped || closingForProbe || connection.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 2_000);
    try {
      const response = await fetch("/api/health", {
        cache: "no-store",
        signal: controller.signal,
      });
      if (!response.ok) {
        throw new Error("Health request failed.");
      }
    } catch {
      if (stopped || closingForProbe || connection.state !== signalR.HubConnectionState.Connected) {
        return;
      }

      closingForProbe = true;
      probeOutage = true;
      onStatus("reconnecting");
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

  function scheduleRecover(): void {
    if (stopped || recoverTimer !== 0) {
      return;
    }

    recoverTimer = window.setTimeout(() => {
      recoverTimer = 0;
      void recover();
    }, 3_000);
  }

  return {
    setStations(next) {
      stationCodes = next;
    },
    setBeforeJoin(reload) {
      beforeJoin = reload;
    },
    start() {
      startHealthProbe();
      return recover();
    },
    async stop() {
      stopped = true;
      stopHealthProbe();
      if (recoverTimer !== 0) {
        window.clearTimeout(recoverTimer);
        recoverTimer = 0;
      }

      await connection.stop();
    },
  };
}
