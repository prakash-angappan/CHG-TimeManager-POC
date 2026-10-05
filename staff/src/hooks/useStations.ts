import { useCallback, useEffect, useState } from "react";
import { ApiError, endSession, getHealth, getStations, markPayment, startSession } from "../services/api";
import { createStationHub } from "../services/signalr";
import { applySessionResponse, applySnapshot, serverOffsetFromHealth, staffErrorMessage } from "../stationState";
import type { ConnectionStatus, SessionResponse, StaffAction, Station } from "../types/station";

export function useStations() {
  const [stations, setStations] = useState<Station[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [serverOffsetMs, setServerOffsetMs] = useState(0);
  const [localNow, setLocalNow] = useState(() => Date.now());
  const [status, setStatus] = useState<ConnectionStatus>("lost");
  const [loadError, setLoadError] = useState<string | null>(null);
  const [pendingStationId, setPendingStationId] = useState<string | null>(null);
  const [actionErrors, setActionErrors] = useState<Record<string, string>>({});

  const reload = useCallback(async () => {
    const before = Date.now();
    const health = await getHealth();
    const after = Date.now();
    const latest = await getStations();
    setServerOffsetMs(serverOffsetFromHealth(health.serverTimeUtc, before, after));
    setStations(latest);
    setLoaded(true);
    setLoadError(null);
    return latest;
  }, []);

  useEffect(() => {
    const timer = window.setInterval(() => setLocalNow(Date.now()), 200);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    const hub = createStationHub(
      (snapshot) => setStations((current) => applySnapshot(current, snapshot)),
      setStatus,
    );
    let active = true;

    hub.setBeforeJoin(async () => {
      try {
        const latest = await reload();
        hub.setStations(latest.map((station) => station.stationCode));
      } catch (error) {
        setLoadError(error instanceof ApiError ? error.message : staffErrorMessage(null));
        throw error;
      }
    });

    void hub.start().catch(() => {
      if (active) {
        setLoadError(staffErrorMessage(null));
        setStatus("lost");
      }
    });

    return () => {
      active = false;
      void hub.stop();
    };
  }, [reload]);

  async function runAction(station: Station, action: StaffAction) {
    if (pendingStationId) {
      return;
    }

    setPendingStationId(station.id);
    setActionErrors((current) => {
      const next = { ...current };
      delete next[station.id];
      return next;
    });

    try {
      const session = await command(station, action);
      setStations((current) => applySessionResponse(current, session));
    } catch (error) {
      const message = error instanceof ApiError ? error.message : staffErrorMessage(null);
      setActionErrors((current) => ({ ...current, [station.id]: message }));
    } finally {
      setPendingStationId(null);
    }
  }

  return {
    stations,
    serverNowMs: localNow + serverOffsetMs,
    status,
    loadError,
    loaded,
    pendingStationId,
    actionErrors,
    runAction,
  };
}

function command(station: Station, action: StaffAction): Promise<SessionResponse> {
  switch (action) {
    case "start":
      return startSession(station.stationCode);
    case "payment":
      return markPayment(station.currentSession!.id);
    case "end":
      return endSession(station.currentSession!.id);
  }
}
