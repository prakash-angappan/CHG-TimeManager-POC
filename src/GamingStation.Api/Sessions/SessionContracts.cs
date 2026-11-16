using GamingStation.Api.Data;

namespace GamingStation.Api.Sessions;

public sealed record StartSessionRequest(string? StationCode);

public sealed record StationInfo(Guid Id, string Name, string StationCode);

public sealed record SessionSummary(
    Guid Id,
    SessionState State,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset ExpiryTimeUtc,
    int DurationSeconds,
    DateTimeOffset CreatedAt);

public sealed record StationResponse(
    Guid Id,
    string Name,
    string StationCode,
    DateTimeOffset CreatedAt,
    SessionSummary? CurrentSession);

public sealed record SessionResponse(
    Guid Id,
    SessionState State,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset ExpiryTimeUtc,
    int DurationSeconds,
    DateTimeOffset CreatedAt,
    StationInfo Station);

public abstract record CommandResult
{
    public sealed record StationList(IReadOnlyList<StationResponse> Stations) : CommandResult;

    public sealed record Station(StationResponse Value) : CommandResult;

    public sealed record Session(SessionResponse Value, int StatusCode) : CommandResult;

    public sealed record Error(int StatusCode, string Title, string Detail) : CommandResult;
}
