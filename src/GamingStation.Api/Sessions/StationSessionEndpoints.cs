namespace GamingStation.Api.Sessions;

public static class StationSessionEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/stations", async (SessionLifecycle lifecycle, CancellationToken cancellationToken) =>
            ToResult(await lifecycle.ListStationsAsync(cancellationToken)));

        app.MapGet("/api/stations/{id:guid}", async (Guid id, SessionLifecycle lifecycle, CancellationToken cancellationToken) =>
            ToResult(await lifecycle.GetStationAsync(id, cancellationToken)));

        app.MapPost("/api/sessions", async (StartSessionRequest? request, SessionLifecycle lifecycle, CancellationToken cancellationToken) =>
            ToResult(await lifecycle.StartAsync(request, cancellationToken)));

        app.MapGet("/api/sessions/{id:guid}", async (Guid id, SessionLifecycle lifecycle, CancellationToken cancellationToken) =>
            ToResult(await lifecycle.GetSessionAsync(id, cancellationToken)));

        app.MapPost("/api/sessions/{id:guid}/payment", async (Guid id, SessionLifecycle lifecycle, CancellationToken cancellationToken) =>
            ToResult(await lifecycle.MarkPaymentAsync(id, cancellationToken)));

        app.MapPost("/api/sessions/{id:guid}/resume", async (Guid id, SessionLifecycle lifecycle, CancellationToken cancellationToken) =>
            ToResult(await lifecycle.ResumeAsync(id, cancellationToken)));

        app.MapPost("/api/sessions/{id:guid}/end", async (Guid id, SessionLifecycle lifecycle, CancellationToken cancellationToken) =>
            ToResult(await lifecycle.EndAsync(id, cancellationToken)));
    }

    private static IResult ToResult(CommandResult result)
    {
        return result switch
        {
            CommandResult.StationList list => Results.Ok(list.Stations),
            CommandResult.Station station => Results.Ok(station.Value),
            CommandResult.Session session when session.StatusCode == StatusCodes.Status201Created
                => Results.Created($"/api/sessions/{session.Value.Id}", session.Value),
            CommandResult.Session session => Results.Ok(session.Value),
            CommandResult.Error error => Results.Problem(
                title: error.Title,
                detail: error.Detail,
                statusCode: error.StatusCode),
            _ => throw new InvalidOperationException($"Unexpected command result '{result.GetType().Name}'.")
        };
    }
}
