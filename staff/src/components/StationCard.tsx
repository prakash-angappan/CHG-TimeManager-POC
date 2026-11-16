import { actionLabels, presentStation } from "../stationState";
import type { StaffAction, Station } from "../types/station";

interface StationCardProps {
  station: Station;
  serverNowMs: number;
  busy: boolean;
  error: string | null;
  onAction: (action: StaffAction) => void;
}

export function StationCard({ station, serverNowMs, busy, error, onAction }: StationCardProps) {
  const view = presentStation(station, serverNowMs);

  return (
    <article className="station-card" aria-busy={busy}>
      <h2>{station.name}</h2>
      <p className="station-code">{station.stationCode}</p>
      <p className="station-state">{view.label}</p>
      {view.countdown && <p className="countdown">{view.countdown}</p>}
      {view.action && (
        <button type="button" disabled={busy} onClick={() => onAction(view.action!)}>
          {actionLabels[view.action]}
        </button>
      )}
      {error && (
        <p className="action-error" role="alert">
          {error}
        </p>
      )}
    </article>
  );
}
