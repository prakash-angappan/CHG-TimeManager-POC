namespace GamingStation.Api.Realtime;

public interface IStationNotifier
{
    Task PublishAsync(StationSnapshot snapshot, CancellationToken cancellationToken);
}
