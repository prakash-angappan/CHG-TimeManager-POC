namespace GamingStation.Api.Data;

public sealed class Session
{
    public Guid Id { get; set; }

    public Guid GamingStationId { get; set; }

    public DateTimeOffset StartTimeUtc { get; set; }

    public DateTimeOffset ExpiryTimeUtc { get; set; }

    public int DurationSeconds { get; set; }

    public SessionState State { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public GamingStation? GamingStation { get; set; }
}
