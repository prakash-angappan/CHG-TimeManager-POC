using Microsoft.AspNetCore.SignalR;

namespace GamingStation.Api.Realtime;

public sealed class SignalRStationNotifier : IStationNotifier
{
    private readonly IHubContext<StationHub> _hub;

    public SignalRStationNotifier(IHubContext<StationHub> hub)
    {
        _hub = hub;
    }

    public Task PublishAsync(StationSnapshot snapshot, CancellationToken cancellationToken)
    {
        return _hub.Clients
            .Group(StationGroups.For(snapshot.StationCode))
            .SendAsync(StationNotifications.StationStateChanged, snapshot, cancellationToken);
    }
}
