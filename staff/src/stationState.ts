import type { SessionResponse, SessionSummary, StaffAction, Station, StationSnapshot } from "./types/station";

const labels: Record<string, string> = {
  running: "GAME RUNNING",
  awaitingPayment: "TIME EXPIRED",
};

export const actionLabels: Record<StaffAction, string> = {
  start: "START SESSION",
  end: "END SESSION",
  payment: "MARK PAYMENT DONE",
};

export interface StationPresentation {
  label: string;
  countdown: string | null;
  action: StaffAction | null;
}

export function serverOffsetFromHealth(serverTimeUtc: string, localBefore: number, localAfter: number): number {
  return Date.parse(serverTimeUtc) - (localBefore + localAfter) / 2;
}

export function formatCountdown(expiryTimeUtc: string, serverNowMs: number): string {
  const remainingMs = Date.parse(expiryTimeUtc) - serverNowMs;
  const totalSeconds = remainingMs <= 0 ? 0 : Math.ceil(remainingMs / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}

export function presentStation(station: Station, serverNowMs: number): StationPresentation {
  const session = station.currentSession;
  if (!session) {
    return { label: "AVAILABLE", countdown: null, action: "start" };
  }

  const action = actionFor(session.state);
  return {
    label: labels[session.state] ?? session.state,
    countdown: session.state === "running" ? formatCountdown(session.expiryTimeUtc, serverNowMs) : null,
    action,
  };
}

export function applySnapshot(stations: Station[], snapshot: StationSnapshot): Station[] {
  const currentSession = openSession(snapshot.currentSession);
  const existing = stations.some((station) => station.stationCode === snapshot.stationCode);
  if (!existing) {
    return [
      ...stations,
      {
        id: snapshot.stationId,
        name: snapshot.stationName,
        stationCode: snapshot.stationCode,
        createdAt: "",
        currentSession,
      },
    ];
  }

  return stations.map((station) =>
    station.stationCode === snapshot.stationCode
      ? { ...station, name: snapshot.stationName, currentSession }
      : station,
  );
}

export function applySessionResponse(stations: Station[], session: SessionResponse): Station[] {
  const currentSession = session.state === "closed" ? null : openSession(session);
  return stations.map((station) =>
    station.id === session.station.id ? { ...station, currentSession } : station,
  );
}

export function staffErrorMessage(status: number | null, title?: string): string {
  if (status === null) {
    return "Unable to contact server.";
  }

  if (status === 409) {
    return title?.trim() || "This station already has an active session.";
  }

  if (status === 404) {
    return "That station or session was not found.";
  }

  return "The server could not complete that action.";
}

function actionFor(state: string): StaffAction | null {
  switch (state) {
    case "running":
      return "end";
    case "awaitingPayment":
      return "payment";
    default:
      return null;
  }
}

function openSession(session: SessionSummary | null): SessionSummary | null {
  if (!session || session.state === "closed") {
    return null;
  }

  return {
    id: session.id,
    state: session.state,
    startTimeUtc: session.startTimeUtc,
    expiryTimeUtc: session.expiryTimeUtc,
    durationSeconds: session.durationSeconds,
  };
}
