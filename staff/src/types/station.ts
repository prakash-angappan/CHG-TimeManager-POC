export type SessionState = "running" | "awaitingPayment" | "readyToResume" | "closed";

export interface SessionSummary {
  id: string;
  state: SessionState;
  startTimeUtc: string;
  expiryTimeUtc: string;
  durationSeconds: number;
}

export interface Station {
  id: string;
  name: string;
  stationCode: string;
  createdAt: string;
  currentSession: SessionSummary | null;
}

export interface SessionResponse extends SessionSummary {
  createdAt: string;
  station: {
    id: string;
    name: string;
    stationCode: string;
  };
}

export interface StationSnapshot {
  stationId: string;
  stationCode: string;
  stationName: string;
  currentSession: SessionSummary | null;
}

export type StaffAction = "start" | "payment" | "end";

export type ConnectionStatus = "connected" | "reconnecting" | "lost";
