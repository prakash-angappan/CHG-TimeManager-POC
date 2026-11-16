using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using GamingStation.Api.Data;
using GamingStation.Api.Realtime;
using StationEntity = GamingStation.Api.Data.GamingStation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GamingStation.Api.Tests;

public class SignalRTests
{
    [Fact]
    public async Task Client_CanConnectAndJoinPs501()
    {
        using var factory = new GamingStationApiFactory();
        await using var listener = await StationListener.ConnectAsync(factory, GamingStationSeeder.StationCode);

        Assert.Equal(HubConnectionState.Connected, listener.Connection.State);
    }

    [Fact]
    public async Task JoinStation_RejectsUnknownStation()
    {
        using var factory = new GamingStationApiFactory();
        await using var listener = await StationListener.ConnectAsync(factory, joinCode: null);

        var exception = await Assert.ThrowsAsync<HubException>(() =>
            listener.Connection.InvokeAsync("JoinStation", "MISSING"));

        Assert.Contains("MISSING", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartSession_PublishesRunningSnapshotAfterCommit()
    {
        var probe = new CommitCheckingNotifierRegistration();
        using var factory = new GamingStationApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(probe.Register);
        });
        await using var listener = await StationListener.ConnectAsync(factory, GamingStationSeeder.StationCode);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/sessions",
            new { stationCode = GamingStationSeeder.StationCode });
        var snapshot = await listener.ReadAsync();
        var notifier = factory.Services.GetRequiredService<CommitCheckingNotifier>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(GamingStationSeeder.StationCode, snapshot.StationCode);
        Assert.Equal(GamingStationSeeder.StationName, snapshot.StationName);
        Assert.Equal(SessionState.Running, snapshot.CurrentSession!.State);
        Assert.Equal(120, snapshot.CurrentSession.DurationSeconds);
        Assert.Equal(snapshot.CurrentSession.StartTimeUtc.AddSeconds(120), snapshot.CurrentSession.ExpiryTimeUtc);
        Assert.Equal(SessionState.Running, notifier.ObservedDatabaseState);
        Assert.Equal(snapshot.CurrentSession.Id, notifier.ObservedSessionId);

        var duplicate = await client.PostAsJsonAsync(
            "/api/sessions",
            new { stationCode = GamingStationSeeder.StationCode });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.False(listener.TryRead(out _));
    }

    [Fact]
    public async Task PaymentResumeAndEnd_PublishLatestSnapshot()
    {
        using var factory = new GamingStationApiFactory();
        await using var listener = await StationListener.ConnectAsync(factory, GamingStationSeeder.StationCode);
        using var client = factory.CreateClient();

        var started = await client.PostAsJsonAsync(
            "/api/sessions",
            new { stationCode = GamingStationSeeder.StationCode });
        var running = await listener.ReadAsync();
        var sessionId = running.CurrentSession!.Id;
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(SessionState.Running, running.CurrentSession.State);

        SetState(factory, sessionId, SessionState.AwaitingPayment);
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        var paid = await client.PostAsync($"/api/sessions/{sessionId}/payment", null);
        var after = DateTimeOffset.UtcNow.AddSeconds(2);
        var resumed = await listener.ReadAsync();
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(sessionId, resumed.CurrentSession!.Id);
        Assert.Equal(SessionState.Running, resumed.CurrentSession.State);
        Assert.Equal(120, resumed.CurrentSession.DurationSeconds);
        Assert.InRange(resumed.CurrentSession.StartTimeUtc, before, after);
        Assert.Equal(resumed.CurrentSession.StartTimeUtc.AddSeconds(120), resumed.CurrentSession.ExpiryTimeUtc);
        Assert.True(resumed.CurrentSession.StartTimeUtc > running.CurrentSession.StartTimeUtc);

        var ended = await client.PostAsync($"/api/sessions/{sessionId}/end", null);
        var available = await listener.ReadAsync();
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        Assert.Equal(running.StationId, available.StationId);
        Assert.Equal(GamingStationSeeder.StationCode, available.StationCode);
        Assert.Null(available.CurrentSession);
    }

    [Fact]
    public async Task OtherStationUpdates_AreNotDeliveredToPs501()
    {
        using var factory = new GamingStationApiFactory();
        AddStation(factory, "XBOX-01", "Xbox #01");
        await using var listener = await StationListener.ConnectAsync(factory, GamingStationSeeder.StationCode);
        await using var outsider = await StationListener.ConnectAsync(factory, joinCode: null);
        using var client = factory.CreateClient();

        var other = await client.PostAsJsonAsync("/api/sessions", new { stationCode = "XBOX-01" });
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        await Task.Delay(300);
        Assert.False(listener.TryRead(out _));
        Assert.False(outsider.TryRead(out _));

        var own = await client.PostAsJsonAsync(
            "/api/sessions",
            new { stationCode = GamingStationSeeder.StationCode });
        var snapshot = await listener.ReadAsync();

        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        Assert.Equal(GamingStationSeeder.StationCode, snapshot.StationCode);
        Assert.Equal(SessionState.Running, snapshot.CurrentSession!.State);
        Assert.False(outsider.TryRead(out _));
    }

    private static void AddStation(GamingStationApiFactory factory, string code, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
        db.GamingStations.Add(new StationEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            StationCode = code,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
    }

    private static void SetState(GamingStationApiFactory factory, Guid sessionId, SessionState state)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
        var session = db.Sessions.Single(item => item.Id == sessionId);
        session.State = state;
        db.SaveChanges();
    }

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

        public static async Task<StationListener> ConnectAsync(WebApplicationFactory<Program> factory, string? joinCode)
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
            if (joinCode is not null)
            {
                await connection.InvokeAsync("JoinStation", joinCode);
            }

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

    private sealed class CommitCheckingNotifier : IStationNotifier
    {
        private readonly IHubContext<StationHub> _hub;
        private readonly IServiceScopeFactory _scopes;

        public CommitCheckingNotifier(IHubContext<StationHub> hub, IServiceScopeFactory scopes)
        {
            _hub = hub;
            _scopes = scopes;
        }

        public StationSnapshot? ObservedSnapshot { get; private set; }

        public SessionState? ObservedDatabaseState { get; private set; }

        public Guid? ObservedSessionId { get; private set; }

        public async Task PublishAsync(StationSnapshot snapshot, CancellationToken cancellationToken)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
            var openSession = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(
                session => session.GamingStationId == snapshot.StationId && session.State != SessionState.Closed,
                cancellationToken);

            ObservedSnapshot = snapshot;
            ObservedDatabaseState = openSession?.State;
            ObservedSessionId = openSession?.Id;

            await _hub.Clients
                .Group(StationGroups.For(snapshot.StationCode))
                .SendAsync(StationNotifications.StationStateChanged, snapshot, cancellationToken);
        }
    }

    private sealed class CommitCheckingNotifierRegistration
    {
        public void Register(IServiceCollection services)
        {
            foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IStationNotifier)).ToList())
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<CommitCheckingNotifier>();
            services.AddSingleton<IStationNotifier>(provider => provider.GetRequiredService<CommitCheckingNotifier>());
        }
    }
}
