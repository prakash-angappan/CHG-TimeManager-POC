import { staffErrorMessage } from "../stationState";
import type { SessionResponse, Station } from "../types/station";

export class ApiError extends Error {
  readonly status: number | null;

  constructor(message: string, status: number | null) {
    super(message);
    this.status = status;
  }
}

export async function getHealth(): Promise<{ serverTimeUtc: string }> {
  const response = await fetch("/api/health", { cache: "no-store" });
  if (!response.ok) {
    throw new ApiError(staffErrorMessage(response.status), response.status);
  }

  return response.json() as Promise<{ serverTimeUtc: string }>;
}

export async function getStations(): Promise<Station[]> {
  const response = await fetch("/api/stations", { cache: "no-store" });
  if (!response.ok) {
    throw new ApiError(staffErrorMessage(response.status), response.status);
  }

  return response.json() as Promise<Station[]>;
}

export async function startSession(stationCode: string): Promise<SessionResponse> {
  return send("/api/sessions", { stationCode });
}

export async function markPayment(sessionId: string): Promise<SessionResponse> {
  return send(`/api/sessions/${sessionId}/payment`);
}

export async function endSession(sessionId: string): Promise<SessionResponse> {
  return send(`/api/sessions/${sessionId}/end`);
}

async function send(url: string, body?: { stationCode: string }): Promise<SessionResponse> {
  let response: Response;
  try {
    response = await fetch(url, {
      method: "POST",
      headers: body ? { "Content-Type": "application/json" } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new ApiError(staffErrorMessage(null), null);
  }

  if (!response.ok) {
    throw new ApiError(await problemMessage(response), response.status);
  }

  return response.json() as Promise<SessionResponse>;
}

async function problemMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { title?: string };
    return staffErrorMessage(response.status, problem.title);
  } catch {
    return staffErrorMessage(response.status);
  }
}
