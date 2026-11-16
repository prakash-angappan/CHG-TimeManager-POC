import type { ConnectionStatus as Status } from "../types/station";

const labels: Record<Status, string> = {
  connected: "CONNECTED",
  reconnecting: "RECONNECTING",
  lost: "CONNECTION LOST",
};

export function ConnectionStatus({ status }: { status: Status }) {
  return (
    <p className={`connection connection-${status}`} role="status">
      {labels[status]}
    </p>
  );
}
