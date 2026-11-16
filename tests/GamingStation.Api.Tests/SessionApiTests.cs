using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GamingStation.Api.Data;
using GamingStation.Api.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GamingStation.Api.Tests;

public class SessionApiTests
{
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    [Fact]
    public async Task GetStations_ReturnsSeededStation()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();

        var stations = await client.GetFromJsonAsync<List<StationResponse>>("/api/stations", Json);

        var station = Assert.Single(stations!);
        Assert.Equal(GamingStationSeeder.StationName, station.Name);
        Assert.Equal(GamingStationSeeder.StationCode, station.StationCode);
        Assert.NotEqual(Guid.Empty, station.Id);
        Assert.Null(station.CurrentSession);
    }

    [Fact]
    public async Task GetStationById_ReturnsStationAndUnknownIdReturnsNotFound()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();
        var stationId = await SeededStationIdAsync(factory);

        var response = await client.GetAsync($"/api/stations/{stationId}");
        var station = await response.Content.ReadFromJsonAsync<StationResponse>(Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(stationId, station!.Id);
        Assert.Equal(GamingStationSeeder.StationCode, station.StationCode);

        var missing = await client.GetAsync($"/api/stations/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task StartSession_UsesServerClockAndConfiguredDuration()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);

        var started = await StartAsync(client, new
        {
            stationCode = GamingStationSeeder.StationCode,
            durationSeconds = 10,
            startTimeUtc = "2000-01-01T00:00:00Z",
            expiryTimeUtc = "2000-01-01T00:01:00Z"
        });

        var after = DateTimeOffset.UtcNow.AddSeconds(2);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var session = await started.Content.ReadFromJsonAsync<SessionResponse>(Json);
        Assert.Equal(SessionState.Running, session!.State);
        Assert.Equal(120, session.DurationSeconds);
        Assert.Equal(TimeSpan.Zero, session.StartTimeUtc.Offset);
        Assert.InRange(session.StartTimeUtc, before, after);
        Assert.Equal(session.StartTimeUtc.AddSeconds(120), session.ExpiryTimeUtc);
        Assert.Equal(session.StartTimeUtc, session.CreatedAt);
        Assert.Equal(GamingStationSeeder.StationCode, session.Station.StationCode);
    }

    [Fact]
    public async Task StartSession_DurationComesFromConfiguration()
    {
        using var factory = new GamingStationApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Session:DefaultDurationSeconds"] = "45"
                });
            });
        });
        using var client = factory.CreateClient();

        var started = await StartAsync(client);
        var session = await started.Content.ReadFromJsonAsync<SessionResponse>(Json);

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(45, session!.DurationSeconds);
        Assert.Equal(session.StartTimeUtc.AddSeconds(45), session.ExpiryTimeUtc);
    }

    [Fact]
    public async Task StartSession_RejectsSecondOpenSession()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();

        var first = await StartAsync(client);
        var second = await StartAsync(client);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Station already has an open session", await ProblemTitleAsync(second));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
        Assert.Single(db.Sessions.Where(session => session.State != SessionState.Closed));
    }

    [Fact]
    public async Task GetSession_ReturnsCreatedSessionAndUnknownIdReturnsNotFound()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();
        var created = await ReadSessionAsync(await StartAsync(client));

        var response = await client.GetAsync($"/api/sessions/{created.Id}");
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.Id, session!.Id);
        Assert.Equal(GamingStationSeeder.StationName, session.Station.Name);

        var missing = await client.GetAsync($"/api/sessions/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Session not found", await ProblemTitleAsync(missing));
    }

    [Fact]
    public async Task Payment_IsRejectedWhileRunningAndAcceptedFromAwaitingPayment()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();
        var session = await ReadSessionAsync(await StartAsync(client));

        var rejected = await client.PostAsync($"/api/sessions/{session.Id}/payment", null);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("Invalid session transition", await ProblemTitleAsync(rejected));
        Assert.Equal(SessionState.Running, (await ReadSessionAsync(await client.GetAsync($"/api/sessions/{session.Id}"))).State);

        SetState(factory, session.Id, SessionState.AwaitingPayment);

        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        var accepted = await client.PostAsync($"/api/sessions/{session.Id}/payment", null);
        var after = DateTimeOffset.UtcNow.AddSeconds(2);
        var paid = await ReadSessionAsync(accepted);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(session.Id, paid.Id);
        Assert.Equal(SessionState.Running, paid.State);
        Assert.Equal(120, paid.DurationSeconds);
        Assert.InRange(paid.StartTimeUtc, before, after);
        Assert.Equal(paid.StartTimeUtc.AddSeconds(120), paid.ExpiryTimeUtc);
        Assert.Equal(session.CreatedAt, paid.CreatedAt);
        Assert.True(paid.StartTimeUtc > session.StartTimeUtc);
    }

    [Fact]
    public async Task Resume_IsRejectedUntilReadyToResumeThenStartsANewWindow()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();
        var session = await ReadSessionAsync(await StartAsync(client));

        var whileRunning = await client.PostAsync($"/api/sessions/{session.Id}/resume", null);
        Assert.Equal(HttpStatusCode.Conflict, whileRunning.StatusCode);

        SetState(factory, session.Id, SessionState.AwaitingPayment);
        var whileAwaitingPayment = await client.PostAsync($"/api/sessions/{session.Id}/resume", null);
        Assert.Equal(HttpStatusCode.Conflict, whileAwaitingPayment.StatusCode);

        SetState(factory, session.Id, SessionState.ReadyToResume);
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        var resumed = await client.PostAsync($"/api/sessions/{session.Id}/resume", null);
        var after = DateTimeOffset.UtcNow.AddSeconds(2);
        var body = await ReadSessionAsync(resumed);

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(session.Id, body.Id);
        Assert.Equal(SessionState.Running, body.State);
        Assert.Equal(120, body.DurationSeconds);
        Assert.InRange(body.StartTimeUtc, before, after);
        Assert.Equal(body.StartTimeUtc.AddSeconds(120), body.ExpiryTimeUtc);
        Assert.Equal(session.CreatedAt, body.CreatedAt);
        Assert.True(body.StartTimeUtc > session.StartTimeUtc);
    }

    [Fact]
    public async Task End_ClosesRunningSessionAndAllowsANewSession()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();
        var first = await ReadSessionAsync(await StartAsync(client));

        var ended = await client.PostAsync($"/api/sessions/{first.Id}/end", null);
        var closed = await ReadSessionAsync(ended);
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        Assert.Equal(SessionState.Closed, closed.State);

        var station = await client.GetFromJsonAsync<StationResponse>($"/api/stations/{first.Station.Id}", Json);
        Assert.Null(station!.CurrentSession);

        var secondResponse = await StartAsync(client);
        var second = await ReadSessionAsync(secondResponse);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(SessionState.Running, second.State);

        var endedAgain = await client.PostAsync($"/api/sessions/{first.Id}/end", null);
        Assert.Equal(HttpStatusCode.Conflict, endedAgain.StatusCode);
    }

    [Fact]
    public async Task End_IsAllowedFromAwaitingPaymentAndReadyToResume()
    {
        using var awaitingFactory = new GamingStationApiFactory();
        using var awaitingClient = awaitingFactory.CreateClient();
        var awaiting = await ReadSessionAsync(await StartAsync(awaitingClient));
        SetState(awaitingFactory, awaiting.Id, SessionState.AwaitingPayment);
        var awaitingEnded = await awaitingClient.PostAsync($"/api/sessions/{awaiting.Id}/end", null);
        Assert.Equal(SessionState.Closed, (await ReadSessionAsync(awaitingEnded)).State);

        using var readyFactory = new GamingStationApiFactory();
        using var readyClient = readyFactory.CreateClient();
        var ready = await ReadSessionAsync(await StartAsync(readyClient));
        SetState(readyFactory, ready.Id, SessionState.ReadyToResume);
        var readyEnded = await readyClient.PostAsync($"/api/sessions/{ready.Id}/end", null);
        Assert.Equal(SessionState.Closed, (await ReadSessionAsync(readyEnded)).State);
    }

    [Fact]
    public async Task ConcurrentStart_CreatesOnlyOneOpenSession()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();

        var first = StartAsync(client);
        var second = StartAsync(client);
        var responses = await Task.WhenAll(first, second);
        var codes = responses.Select(response => response.StatusCode).OrderBy(code => code).ToArray();

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], codes);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
        Assert.Single(db.Sessions);
        Assert.Equal(SessionState.Running, db.Sessions.Single().State);
    }

    [Fact]
    public async Task StartSession_RejectsUnknownStationAndMissingCode()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();

        var unknown = await StartAsync(client, new { stationCode = "UNKNOWN" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var missing = await client.PostAsJsonAsync("/api/sessions", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    private static async Task<HttpResponseMessage> StartAsync(HttpClient client, object? body = null)
    {
        return await client.PostAsJsonAsync("/api/sessions", body ?? new { stationCode = GamingStationSeeder.StationCode });
    }

    private static async Task<SessionResponse> ReadSessionAsync(HttpResponseMessage response)
    {
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(Json);
        Assert.NotNull(session);
        return session;
    }

    private static async Task<string?> ProblemTitleAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemTitle>(Json);
        return problem?.Title;
    }

    private static async Task<Guid> SeededStationIdAsync(GamingStationApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
        return await db.GamingStations.Select(station => station.Id).SingleAsync();
    }

    private static void SetState(GamingStationApiFactory factory, Guid sessionId, SessionState state)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
        var session = db.Sessions.Single(item => item.Id == sessionId);
        session.State = state;
        db.SaveChanges();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record ProblemTitle(string? Title);
}
