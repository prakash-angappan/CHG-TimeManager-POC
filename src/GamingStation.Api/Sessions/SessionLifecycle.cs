using GamingStation.Api.Data;
using GamingStation.Api.Realtime;
using StationEntity = GamingStation.Api.Data.GamingStation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamingStation.Api.Sessions;

public sealed class SessionLifecycle
{
    private const int SqliteConstraint = 19;
    private readonly GamingStationDbContext _db;
    private readonly TimeProvider _clock;
    private readonly SessionOptions _options;
    private readonly IStationNotifier _notifier;
    private readonly ILogger<SessionLifecycle> _logger;

    public SessionLifecycle(
        GamingStationDbContext db,
        TimeProvider clock,
        IOptions<SessionOptions> options,
        IStationNotifier notifier,
        ILogger<SessionLifecycle> logger)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<CommandResult> ListStationsAsync(CancellationToken cancellationToken)
    {
        var stations = await _db.GamingStations
            .AsNoTracking()
            .Include(station => station.Sessions.Where(session => session.State != SessionState.Closed))
            .OrderBy(station => station.StationCode)
            .ToListAsync(cancellationToken);

        return new CommandResult.StationList(stations.Select(ToStationResponse).ToArray());
    }

    public async Task<CommandResult> GetStationAsync(Guid stationId, CancellationToken cancellationToken)
    {
        var station = await _db.GamingStations
            .AsNoTracking()
            .Include(item => item.Sessions.Where(session => session.State != SessionState.Closed))
            .SingleOrDefaultAsync(item => item.Id == stationId, cancellationToken);

        if (station is null)
        {
            return NotFound("Station not found", $"No station exists with id '{stationId}'.");
        }

        return new CommandResult.Station(ToStationResponse(station));
    }

    public async Task<CommandResult> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(sessionId, tracking: false, cancellationToken);
        if (session?.GamingStation is null)
        {
            return SessionNotFound(sessionId);
        }

        return new CommandResult.Session(ToSessionResponse(session, session.GamingStation), StatusCodes.Status200OK);
    }

    public async Task<CommandResult> StartAsync(StartSessionRequest? request, CancellationToken cancellationToken)
    {
        var stationCode = request?.StationCode?.Trim();
        if (string.IsNullOrEmpty(stationCode))
        {
            return new CommandResult.Error(
                StatusCodes.Status400BadRequest,
                "Station code is required",
                "Provide a stationCode.");
        }

        var station = await _db.GamingStations
            .SingleOrDefaultAsync(item => item.StationCode == stationCode, cancellationToken);
        if (station is null)
        {
            return NotFound("Station not found", $"No station exists with code '{stationCode}'.");
        }

        if (await HasOpenSessionAsync(station.Id, cancellationToken))
        {
            return OpenSessionConflict(stationCode);
        }

        var now = _clock.GetUtcNow();
        var duration = _options.DefaultDurationSeconds;
        var session = new Session
        {
            Id = Guid.NewGuid(),
            GamingStationId = station.Id,
            StartTimeUtc = now,
            ExpiryTimeUtc = now.AddSeconds(duration),
            DurationSeconds = duration,
            State = SessionState.Running,
            CreatedAt = now
        };
        _db.Sessions.Add(session);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            return OpenSessionConflict(stationCode);
        }

        await PublishStationAsync(station, cancellationToken);
        return new CommandResult.Session(ToSessionResponse(session, station), StatusCodes.Status201Created);
    }

    public async Task<CommandResult> MarkPaymentAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(sessionId, tracking: true, cancellationToken);
        if (session?.GamingStation is null)
        {
            return SessionNotFound(sessionId);
        }

        if (session.State != SessionState.AwaitingPayment)
        {
            return InvalidTransition(
                "Payment can be marked only when the session is awaiting payment.",
                session.State);
        }

        GrantRunningWindow(session);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishStationAsync(session.GamingStation, cancellationToken);

        return new CommandResult.Session(ToSessionResponse(session, session.GamingStation), StatusCodes.Status200OK);
    }

    public async Task<CommandResult> ResumeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(sessionId, tracking: true, cancellationToken);
        if (session?.GamingStation is null)
        {
            return SessionNotFound(sessionId);
        }

        if (session.State != SessionState.ReadyToResume)
        {
            return InvalidTransition(
                "A session can be resumed only when it is ready to resume.",
                session.State);
        }

        GrantRunningWindow(session);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishStationAsync(session.GamingStation, cancellationToken);

        return new CommandResult.Session(ToSessionResponse(session, session.GamingStation), StatusCodes.Status200OK);
    }

    public async Task<CommandResult> EndAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(sessionId, tracking: true, cancellationToken);
        if (session?.GamingStation is null)
        {
            return SessionNotFound(sessionId);
        }

        if (session.State == SessionState.Closed)
        {
            return InvalidTransition("A closed session cannot be ended again.", session.State);
        }

        session.State = SessionState.Closed;
        await _db.SaveChangesAsync(cancellationToken);
        await PublishStationAsync(session.GamingStation, cancellationToken);

        return new CommandResult.Session(ToSessionResponse(session, session.GamingStation), StatusCodes.Status200OK);
    }

    private void GrantRunningWindow(Session session)
    {
        var now = _clock.GetUtcNow();
        var duration = _options.DefaultDurationSeconds;
        session.StartTimeUtc = now;
        session.ExpiryTimeUtc = now.AddSeconds(duration);
        session.DurationSeconds = duration;
        session.State = SessionState.Running;
    }

    private async Task<CommandResult> TransitionAsync(
        Guid sessionId,
        SessionState requiredState,
        SessionState nextState,
        string rule,
        CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(sessionId, tracking: true, cancellationToken);
        if (session?.GamingStation is null)
        {
            return SessionNotFound(sessionId);
        }

        if (session.State != requiredState)
        {
            return InvalidTransition(rule, session.State);
        }

        session.State = nextState;
        await _db.SaveChangesAsync(cancellationToken);
        await PublishStationAsync(session.GamingStation, cancellationToken);
        return new CommandResult.Session(ToSessionResponse(session, session.GamingStation), StatusCodes.Status200OK);
    }

    private async Task PublishStationAsync(StationEntity station, CancellationToken cancellationToken)
    {
        var openSession = await _db.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                session => session.GamingStationId == station.Id && session.State != SessionState.Closed,
                cancellationToken);

        var snapshot = new StationSnapshot(
            station.Id,
            station.StationCode,
            station.Name,
            openSession is null
                ? null
                : new SessionSnapshot(
                    openSession.Id,
                    openSession.State,
                    openSession.StartTimeUtc,
                    openSession.ExpiryTimeUtc,
                    openSession.DurationSeconds));

        try
        {
            await _notifier.PublishAsync(snapshot, cancellationToken);
            _logger.LogInformation(
                "Published StationStateChanged for station {StationCode}. Session state {SessionState}.",
                station.StationCode,
                snapshot.CurrentSession?.State.ToString() ?? "available");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to publish state for station {StationCode} after the database commit.",
                station.StationCode);
        }
    }

    public async Task<int> ExpireDueSessionsAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var runningSessions = await _db.Sessions
            .AsNoTracking()
            .Where(session => session.State == SessionState.Running)
            .Select(session => new DueSession(session.Id, session.GamingStationId, session.ExpiryTimeUtc))
            .ToListAsync(cancellationToken);
        var dueSessions = runningSessions
            .Where(session => session.ExpiryTimeUtc <= now)
            .ToList();

        var transitioned = 0;
        foreach (var due in dueSessions)
        {
            _logger.LogInformation(
                "Expired session {SessionId} detected. ExpiryTimeUtc {ExpiryTimeUtc}.",
                due.Id,
                due.ExpiryTimeUtc);

            // State and the original expiry are both in the predicate so a concurrent
            // end or resume that already committed matches zero rows and is left alone.
            var updated = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "Sessions"
                SET "State" = 'AwaitingPayment'
                WHERE "Id" = {due.Id}
                  AND "State" = 'Running'
                  AND "ExpiryTimeUtc" = {due.ExpiryTimeUtc}
                """,
                cancellationToken);

            if (updated == 0)
            {
                _logger.LogInformation(
                    "Session {SessionId} was no longer a running session with expiry {ExpiryTimeUtc}. Expiry skipped.",
                    due.Id,
                    due.ExpiryTimeUtc);
                continue;
            }

            transitioned++;
            _logger.LogInformation("Session {SessionId} transitioned to AwaitingPayment.", due.Id);

            var station = await _db.GamingStations
                .AsNoTracking()
                .SingleAsync(item => item.Id == due.GamingStationId, cancellationToken);
            await PublishStationAsync(station, cancellationToken);
        }

        return transitioned;
    }

    private async Task<Session?> FindSessionAsync(Guid sessionId, bool tracking, CancellationToken cancellationToken)
    {
        var query = _db.Sessions.Include(session => session.GamingStation).AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
    }

    private Task<bool> HasOpenSessionAsync(Guid stationId, CancellationToken cancellationToken)
    {
        return _db.Sessions.AnyAsync(
            session => session.GamingStationId == stationId && session.State != SessionState.Closed,
            cancellationToken);
    }

    private static StationResponse ToStationResponse(StationEntity station)
    {
        var current = station.Sessions.SingleOrDefault(session => session.State != SessionState.Closed);
        return new StationResponse(
            station.Id,
            station.Name,
            station.StationCode,
            station.CreatedAt,
            current is null ? null : ToSummary(current));
    }

    private static SessionSummary ToSummary(Session session)
    {
        return new SessionSummary(
            session.Id,
            session.State,
            session.StartTimeUtc,
            session.ExpiryTimeUtc,
            session.DurationSeconds,
            session.CreatedAt);
    }

    private static SessionResponse ToSessionResponse(Session session, StationEntity station)
    {
        return new SessionResponse(
            session.Id,
            session.State,
            session.StartTimeUtc,
            session.ExpiryTimeUtc,
            session.DurationSeconds,
            session.CreatedAt,
            new StationInfo(station.Id, station.Name, station.StationCode));
    }

    private static CommandResult NotFound(string title, string detail)
    {
        return new CommandResult.Error(StatusCodes.Status404NotFound, title, detail);
    }

    private static CommandResult SessionNotFound(Guid sessionId)
    {
        return NotFound("Session not found", $"No session exists with id '{sessionId}'.");
    }

    private static CommandResult OpenSessionConflict(string stationCode)
    {
        return new CommandResult.Error(
            StatusCodes.Status409Conflict,
            "Station already has an open session",
            $"Station '{stationCode}' already has an open session.");
    }

    private static CommandResult InvalidTransition(string rule, SessionState current)
    {
        return new CommandResult.Error(
            StatusCodes.Status409Conflict,
            "Invalid session transition",
            $"{rule} Current state is {current}.");
    }

    private sealed record DueSession(Guid Id, Guid GamingStationId, DateTimeOffset ExpiryTimeUtc);

    private static bool IsUniqueConstraint(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SqliteException sqlite && sqlite.SqliteErrorCode == SqliteConstraint)
            {
                return true;
            }
        }

        return false;
    }
}
