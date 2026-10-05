using GamingStation.Api.Data;

namespace GamingStation.Api.Realtime;

public sealed record SessionSnapshot(
    Guid Id,
    SessionState State,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset ExpiryTimeUtc,
    int DurationSeconds);

public sealed record StationSnapshot(
    Guid StationId,
    string StationCode,
    string StationName,
    SessionSnapshot? CurrentSession);
