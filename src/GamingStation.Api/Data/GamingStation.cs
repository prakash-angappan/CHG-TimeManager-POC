namespace GamingStation.Api.Data;

public sealed class GamingStation
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string StationCode { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}
