import { describe, expect, it } from "vitest";
import {
  applySessionResponse,
  applySnapshot,
  formatCountdown,
  presentStation,
  serverOffsetFromHealth,
  staffErrorMessage,
} from "./stationState";
import type { Station } from "./types/station";

const station: Station = {
  id: "station-1",
  name: "PS5 #01",
  stationCode: "PS5-01",
  createdAt: "2026-09-23T15:00:33.2569469+00:00",
  currentSession: null,
};

const other: Station = {
  ...station,
  id: "station-2",
  name: "Xbox #01",
  stationCode: "XBOX-01",
};

describe("presentStation", () => {
  it("offers start when the station is available", () => {
    expect(presentStation(station, 0)).toEqual({
      label: "AVAILABLE",
      countdown: null,
      action: "start",
    });
  });

  it("keeps a running session running when the countdown reaches zero", () => {
    const running: Station = {
      ...station,
      currentSession: {
        id: "session-1",
        state: "running",
        startTimeUtc: "2026-09-28T15:00:00.000Z",
        expiryTimeUtc: "2026-09-28T15:02:00.000Z",
        durationSeconds: 120,
      },
    };

    const view = presentStation(running, Date.parse("2026-09-28T15:02:05.000Z"));

    expect(view.label).toBe("GAME RUNNING");
    expect(view.countdown).toBe("00:00");
    expect(view.action).toBe("end");
  });

  it("offers payment for an expired session and does not offer resume", () => {
    const awaiting: Station = {
      ...station,
      currentSession: {
        id: "session-1",
        state: "awaitingPayment",
        startTimeUtc: "2026-09-28T15:00:00.000Z",
        expiryTimeUtc: "2026-09-28T15:02:00.000Z",
        durationSeconds: 120,
      },
    };
    const ready: Station = {
      ...awaiting,
      currentSession: { ...awaiting.currentSession!, state: "readyToResume" },
    };

    expect(presentStation(awaiting, 0)).toEqual({
      label: "TIME EXPIRED",
      countdown: null,
      action: "payment",
    });
    expect(presentStation(ready, 0).action).toBeNull();
    expect(presentStation(ready, 0).label).not.toBe("READY TO RESUME");
  });
});

describe("formatCountdown", () => {
  it("formats the remaining server time", () => {
    const expiry = "2026-09-28T15:02:00.000Z";
    const now = Date.parse("2026-09-28T15:00:18.100Z");

    expect(formatCountdown(expiry, now)).toBe("01:42");
  });
});

describe("serverOffsetFromHealth", () => {
  it("uses the midpoint of the health request", () => {
    const offset = serverOffsetFromHealth("2026-09-28T15:00:10.000Z", 1_000, 1_400);

    expect(offset).toBe(Date.parse("2026-09-28T15:00:10.000Z") - 1_200);
  });
});

describe("station updates", () => {
  it("updates only the station named by a snapshot", () => {
    const next = applySnapshot([station, other], {
      stationId: station.id,
      stationCode: "PS5-01",
      stationName: "PS5 #01",
      currentSession: {
        id: "session-1",
        state: "running",
        startTimeUtc: "2026-09-28T15:00:00.000Z",
        expiryTimeUtc: "2026-09-28T15:02:00.000Z",
        durationSeconds: 120,
      },
    });

    expect(next[0].currentSession?.state).toBe("running");
    expect(next[1].currentSession).toBeNull();
  });

  it("clears the station when the command response is closed", () => {
    const running: Station = {
      ...station,
      currentSession: {
        id: "session-1",
        state: "running",
        startTimeUtc: "2026-09-28T15:00:00.000Z",
        expiryTimeUtc: "2026-09-28T15:02:00.000Z",
        durationSeconds: 120,
      },
    };

    const next = applySessionResponse([running, other], {
      id: "session-1",
      state: "closed",
      startTimeUtc: "2026-09-28T15:00:00.000Z",
      expiryTimeUtc: "2026-09-28T15:02:00.000Z",
      durationSeconds: 120,
      createdAt: "2026-09-28T15:00:00.000Z",
      station: { id: station.id, name: station.name, stationCode: station.stationCode },
    });

    expect(next[0].currentSession).toBeNull();
    expect(next[1].currentSession).toBeNull();
  });
});

describe("staffErrorMessage", () => {
  it("maps common failures to staff-facing text", () => {
    expect(staffErrorMessage(null)).toBe("Unable to contact server.");
    expect(staffErrorMessage(409, "Station already has an open session")).toBe(
      "Station already has an open session",
    );
    expect(staffErrorMessage(404)).toBe("That station or session was not found.");
    expect(staffErrorMessage(500)).toBe("The server could not complete that action.");
  });
});
