import { afterEach, describe, expect, it, vi } from "vitest";
import { createStationHub } from "./signalr";
import type { ConnectionStatus } from "../types/station";

const { connection, handlers } = vi.hoisted(() => {
  const handlers = {
    close: [] as Array<() => void>,
  };
  const connection = {
    state: "Disconnected",
    start: vi.fn(async () => {
      connection.state = "Connected";
    }),
    stop: vi.fn(async () => {
      connection.state = "Disconnected";
      handlers.close.forEach((handler) => handler());
    }),
    invoke: vi.fn(async () => undefined),
    on: vi.fn(),
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: (handler: () => void) => {
      handlers.close.push(handler);
    },
  };
  return { connection, handlers };
});

vi.mock("@microsoft/signalr", () => {
  class HubConnectionBuilder {
    withUrl() {
      return this;
    }

    withAutomaticReconnect() {
      return this;
    }

    build() {
      return connection;
    }
  }

  return {
    HubConnectionBuilder,
    HubConnectionState: {
      Disconnected: "Disconnected",
      Connecting: "Connecting",
      Connected: "Connected",
      Disconnecting: "Disconnecting",
      Reconnecting: "Reconnecting",
    },
  };
});

function useBrowserTimers() {
  vi.useFakeTimers();
  vi.stubGlobal("window", {
    setInterval: (...args: Parameters<typeof setInterval>) => setInterval(...args),
    clearInterval: (...args: Parameters<typeof clearInterval>) => clearInterval(...args),
    setTimeout: (...args: Parameters<typeof setTimeout>) => setTimeout(...args),
    clearTimeout: (...args: Parameters<typeof clearTimeout>) => clearTimeout(...args),
  });
}

describe("station hub health probe", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.clearAllMocks();
    handlers.close.length = 0;
    connection.state = "Disconnected";
  });

  it("keeps one probe and ignores a successful health response", async () => {
    useBrowserTimers();
    const fetchMock = vi.fn(async () => new Response("{}", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const statuses: ConnectionStatus[] = [];
    const snapshots: unknown[] = [];
    const hub = createStationHub(
      (snapshot) => snapshots.push(snapshot),
      (status) => statuses.push(status),
    );

    await Promise.all([hub.start(), hub.start()]);
    await vi.advanceTimersByTimeAsync(5_000);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith("/api/health", expect.objectContaining({ cache: "no-store" }));
    expect(statuses.every((status) => status === "connected")).toBe(true);
    expect(snapshots).toEqual([]);
    expect(connection.stop).not.toHaveBeenCalled();

    await hub.stop();
    await vi.advanceTimersByTimeAsync(10_000);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("shows reconnecting when the health request times out and keeps station data", async () => {
    useBrowserTimers();
    let signal: AbortSignal | undefined;
    let offline = false;
    vi.stubGlobal(
      "fetch",
      vi.fn((_url: string, init?: RequestInit) => {
        signal = init?.signal ?? undefined;
        return new Promise((_resolve, reject) => {
          init?.signal?.addEventListener("abort", () => reject(new Error("aborted")));
        });
      }),
    );
    const statuses: ConnectionStatus[] = [];
    const snapshots: unknown[] = [];
    const hub = createStationHub(
      (snapshot) => snapshots.push(snapshot),
      (status) => statuses.push(status),
    );
    hub.setBeforeJoin(async () => {
      if (offline) {
        throw new Error("offline");
      }
    });

    await hub.start();
    offline = true;
    await vi.advanceTimersByTimeAsync(5_000);
    expect(signal?.aborted).toBe(false);
    await vi.advanceTimersByTimeAsync(2_000);

    expect(signal?.aborted).toBe(true);
    expect(statuses.at(-1)).toBe("reconnecting");
    expect(snapshots).toEqual([]);
    expect(connection.stop).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(3_000);
    expect(statuses.at(-1)).toBe("reconnecting");
  });
});
