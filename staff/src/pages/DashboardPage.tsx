import { ConnectionStatus } from "../components/ConnectionStatus";
import { StationCard } from "../components/StationCard";
import { useStations } from "../hooks/useStations";

export function DashboardPage() {
  const { stations, serverNowMs, status, loadError, loaded, pendingStationId, actionErrors, runAction } = useStations();

  return (
    <>
      <ConnectionStatus status={status} />
      <main>
        <h1>Stations</h1>
        {loadError && (
          <p className="banner" role="alert">
            {loadError}
          </p>
        )}
        {loaded && stations.length === 0 && !loadError && <p className="empty">No stations.</p>}
        <div className="station-list">
          {stations.map((station) => (
            <StationCard
              key={station.id}
              station={station}
              serverNowMs={serverNowMs}
              busy={pendingStationId === station.id}
              error={actionErrors[station.id] ?? null}
              onAction={(action) => {
                void runAction(station, action);
              }}
            />
          ))}
        </div>
      </main>
    </>
  );
}
