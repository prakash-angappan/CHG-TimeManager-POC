using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using GamingStation.Api.Data;
using GamingStation.Api.Realtime;
using GamingStation.Api.Sessions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GamingStation.Api.Tests;

public class SessionExpiryTests
{
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    [Fact]
    public void ExpiryCheckInterval_DefaultsToOneSecond()
    {
        using var factory = new GamingStationApiFactory();
        var options = factory.Services.GetRequiredService<IOptions<SessionOptions>>().Value;

        Assert.Equal(120, options.DefaultDurationSeconds);
        Assert.Equal(1, options.ExpiryCheckIntervalSeconds);
    }

    [Fact]
    public async Task FutureRunningSession_StaysRunning()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var session = await StartAsync(client);

        var transitioned = await ExpireAsync(factory);

        var station = await GetStationAsync(client);
        Assert.Equal(0, transitioned);
        Assert.Equal(SessionState.Running, station.CurrentSession!.State);
        Assert.Equal(session.Id, station.CurrentSession.Id);
        Assert.True(station.CurrentSession.ExpiryTimeUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ExpiredRunningSession_BecomesAwaitingPaymentAndStaysOccupied()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var session = await StartAsync(client);
        var times = Backdate(factory, session.Id, TimeSpan.FromSeconds(-1));

        var transitioned = await ExpireAsync(factory);

        var station = await GetStationAsync(client);
        Assert.Equal(1, transitioned);
        Assert.NotNull(station.CurrentSession);
        Assert.Equal(session.Id, station.CurrentSession.Id);
        Assert.Equal(SessionState.AwaitingPayment, station.CurrentSession.State);
        Assert.Equal(times.StartTimeUtc, station.CurrentSession.StartTimeUtc);
        Assert.Equal(times.ExpiryTimeUtc, station.CurrentSession.ExpiryTimeUtc);
        Assert.Equal(times.DurationSeconds, station.CurrentSession.DurationSeconds);
    }

    [Fact]
    public async Task Expiry_PublishesOneAwaitingPaymentSnapshot()
    {
        using var factory = CreateFactory();
        await using var listener = await StationListener.ConnectAsync(factory, GamingStationSeeder.StationCode);
        using var client = factory.CreateClient();
        var session = await StartAsync(client);
        var started = await listener.ReadAsync();
        var times = Backdate(factory, session.Id, TimeSpan.FromSeconds(-1));

        Assert.Equal(1, await ExpireAsync(factory));
        var snapshot = await listener.ReadAsync();
        Assert.Equal(0, await ExpireAsync(factory));
        await Task.Delay(200);

        Assert.Equal(SessionState.Running, started.CurrentSession!.State);
        Assert.Equal(GamingStationSeeder.StationCode, snapshot.StationCode);
        Assert.Equal(GamingStationSeeder.StationName, snapshot.StationName);
        Assert.NotEqual(Guid.Empty, snapshot.StationId);
        Assert.Equal(session.Id, snapshot.CurrentSession!.Id);
        Assert.Equal(SessionState.AwaitingPayment, snapshot.CurrentSession.State);
        Assert.Equal(times.StartTimeUtc, snapshot.CurrentSession.StartTimeUtc);
        Assert.Equal(times.ExpiryTimeUtc, snapshot.CurrentSession.ExpiryTimeUtc);
        Assert.Equal(times.DurationSeconds, snapshot.CurrentSession.DurationSeconds);
        Assert.False(listener.TryRead(out _));

        var station = await GetStationAsync(client);
        Assert.Equal(SessionState.AwaitingPayment, station.CurrentSession!.State);
    }

    [Fact]
    public async Task AwaitingPaymentSession_IsNotProcessedAgain()
    {
        using var factory = CreateFactory();
        await using var listener = await StationListener.ConnectAsync(factory, GamingStationSeeder.StationCode);
        using var client = factory.CreateClient();
        var session = await StartAsync(client);
        await listener.ReadAsync();
        Backdate(factory, session.Id, TimeSpan.FromSeconds(-5), SessionState.AwaitingPayment);

        Assert.Equal(0, await ExpireAsync(factory));
        await Task.Delay(200);

        var station = await GetStationAsync(client);
        Assert.Equal(SessionState.AwaitingPayment, station.CurrentSession!.State);
        Assert.Equal(session.Id, station.CurrentSession.Id);
        Assert.False(listener.TryRead(out _));
    }

    [Fact]
    public async Task ClosedSession_IsNotProcessed()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var session = await StartAsync(client);
        Backdate(factory, session.Id, TimeSpan.FromSeconds(-5), SessionState.Closed);

        Assert.Equal(0, await ExpireAsync(factory));

        var stored = await ReadStoredAsync(factory, session.Id);
        var station = await GetStationAsync(client);
        Assert.Equal(SessionState.Closed, stored.State);
        Assert.Null(station.CurrentSession);
    }

    [Fact]
    public async Task EndBeforeExpiry_PreventsAwaitingPayment()
    {
        using var factory = CreateFactory();
        await using var listener = await StationListener.ConnectAsync(factory, GamingStationSeeder.StationCode);
        using var client = factory.CreateClient();
        var session = await StartAsync(client);
        await listener.ReadAsync();
        Backdate(factory, session.Id, TimeSpan.FromSeconds(-1));

        var ended = await client.PostAsync($"/api/sessions/{session.Id}/end", null);
        var available = await listener.ReadAsync();
        Assert.Equal(0, await ExpireAsync(factory));
        await Task.Delay(200);

        var stored = await ReadStoredAsync(factory, session.Id);
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        Assert.Null(available.CurrentSession);
        Assert.Equal(SessionState.Closed, stored.State);
        Assert.False(listener.TryRead(out _));
    }

    [Fact]
    public async Task Resume_ReplacesTheExpiryWindow()
    {
        using var factory = CreateFactory();
        await using var listener = await StationListener.ConnectAsync(factory, GamingStationSeeder.StationCode);
        using var client = factory.CreateClient();
        var session = await StartAsync(client);
        await listener.ReadAsync();
        Backdate(factory, session.Id, TimeSpan.FromSeconds(-30), SessionState.ReadyToResume);

        var resumed = await client.PostAsync($"/api/sessions/{session.Id}/resume", null);
        var running = await listener.ReadAsync();
        var before = DateTimeOffset.UtcNow;
        Assert.Equal(0, await ExpireAsync(factory));
        await Task.Delay(200);

        var station = await GetStationAsync(client);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(SessionState.Running, running.CurrentSession!.State);
        Assert.Equal(session.Id, station.CurrentSession!.Id);
        Assert.Equal(SessionState.Running, station.CurrentSession.State);
        Assert.True(station.CurrentSession.ExpiryTimeUtc >= before.AddSeconds(120).AddSeconds(-2));
        Assert.Equal(station.CurrentSession.StartTimeUtc.AddSeconds(120), station.CurrentSession.ExpiryTimeUtc);
        Assert.False(listener.TryRead(out _));
    }

    [Fact]
    public async Task Restart_RecoversAlreadyExpiredRunningSession()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gamingstation-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Guid sessionId;
            SessionTimes times;
            using (var setup = new GamingStationApiFactory(directory).WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(DisableExpirySweep)))
            {
                using var client = setup.CreateClient();
                var session = await StartAsync(client);
                sessionId = session.Id;
                times = Backdate(setup, sessionId, TimeSpan.FromSeconds(-1));
            }

            using var restarted = new GamingStationApiFactory(directory);
            using var restartedClient = restarted.CreateClient();
            StationResponse? station = null;
            for (var attempt = 0; attempt < 50; attempt++)
            {
                station = await GetStationAsync(restartedClient);
                if (station.CurrentSession?.State == SessionState.AwaitingPayment)
                {
                    break;
                }

                await Task.Delay(100);
            }

            Assert.NotNull(station!.CurrentSession);
            Assert.Equal(sessionId, station.CurrentSession.Id);
            Assert.Equal(SessionState.AwaitingPayment, station.CurrentSession.State);
            Assert.Equal(times.StartTimeUtc, station.CurrentSession.StartTimeUtc);
            Assert.Equal(times.ExpiryTimeUtc, station.CurrentSession.ExpiryTimeUtc);
            Assert.Equal(120, station.CurrentSession.DurationSeconds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new GamingStationApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(DisableExpirySweep));
    }

    private static void DisableExpirySweep(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(service =>
            service.ServiceType == typeof(IHostedService)
            && service.ImplementationType == typeof(SessionExpiryService));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }
    }

    private static async Task<SessionResponse> StartAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/sessions",
            new { stationCode = GamingStationSeeder.StationCode });
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(Json);
        return session!;
    }

    private static async Task<StationResponse> GetStationAsync(HttpClient client)
    {
        var stations = await client.GetFromJsonAsync<List<StationResponse>>("/api/stations", Json);
        return Assert.Single(stations!);
    }

    private static async Task<int> ExpireAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<SessionLifecycle>();
        return await lifecycle.ExpireDueSessionsAsync(CancellationToken.None);
    }

    private static async Task<Session> ReadStoredAsync(WebApplicationFactory<Program> factory, Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
        return await db.Sessions.AsNoTracking().SingleAsync(session => session.Id == sessionId);
    }

    private static SessionTimes Backdate(
        WebApplicationFactory<Program> factory,
        Guid sessionId,
        TimeSpan expiryOffset,
        SessionState? state = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
        var session = db.Sessions.Single(item => item.Id == sessionId);
        session.ExpiryTimeUtc = DateTimeOffset.UtcNow.Add(expiryOffset);
        if (state is not null)
        {
            session.State = state.Value;
        }

        db.SaveChanges();
        return new SessionTimes(session.StartTimeUtc, session.ExpiryTimeUtc, session.DurationSeconds);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record SessionTimes(DateTimeOffset StartTimeUtc, DateTimeOffset ExpiryTimeUtc, int DurationSeconds);

    private sealed class StationListener : IAsyncDisposable
    {
        private readonly Channel<StationSnapshot> _messages = Channel.CreateUnbounded<StationSnapshot>();

        private StationListener(HubConnection connection)
        {
            Connection = connection;
            connection.On<StationSnapshot>(StationNotifications.StationStateChanged, snapshot =>
            {
                _messages.Writer.TryWrite(snapshot);
            });
        }

        public HubConnection Connection { get; }

        public static async Task<StationListener> ConnectAsync(WebApplicationFactory<Program> factory, string stationCode)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/stations"), options =>
                {
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                })
                .AddJsonProtocol(options =>
                {
                    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    options.PayloadSerializerOptions.Converters.Add(
                        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                })
                .Build();

            var listener = new StationListener(connection);
            await connection.StartAsync();
            await connection.InvokeAsync("JoinStation", stationCode);
            return listener;
        }

        public async Task<StationSnapshot> ReadAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await _messages.Reader.ReadAsync(timeout.Token);
        }

        public bool TryRead(out StationSnapshot? snapshot)
        {
            return _messages.Reader.TryRead(out snapshot);
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
        }
    }
}
