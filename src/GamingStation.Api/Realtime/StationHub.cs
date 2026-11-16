using GamingStation.Api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GamingStation.Api.Realtime;

public sealed class StationHub : Hub
{
    private readonly GamingStationDbContext _db;

    public StationHub(GamingStationDbContext db)
    {
        _db = db;
    }

    public async Task JoinStation(string? stationCode)
    {
        var code = stationCode?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            throw new HubException("Station code is required.");
        }

        var exists = await _db.GamingStations.AnyAsync(station => station.StationCode == code);
        if (!exists)
        {
            throw new HubException($"No station exists with code '{code}'.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, StationGroups.For(code));
    }
}
