namespace GamingStation.Api.Data;

public static class GamingStationSeeder
{
    public const string StationName = "PS5 #01";
    public const string StationCode = "PS5-01";

    public static void Seed(GamingStationDbContext db, TimeProvider clock)
    {
        var exists = db.GamingStations.Any(station => station.StationCode == StationCode);
        if (exists)
        {
            return;
        }

        db.GamingStations.Add(new GamingStation
        {
            Id = Guid.NewGuid(),
            Name = StationName,
            StationCode = StationCode,
            CreatedAt = clock.GetUtcNow()
        });
        db.SaveChanges();
    }
}
