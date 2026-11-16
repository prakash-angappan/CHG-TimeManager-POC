using System.Data.Common;
using GamingStation.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GamingStation.Api.Tests;

public class DatabaseTests
{
    [Fact]
    public void EmptyDataDirectory_UsesLocalApplicationData()
    {
        var options = new DatabaseOptions();

        var directory = options.ResolveDataDirectory(@"C:\app");

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamingStation"),
            directory);
    }

    [Fact]
    public void RelativeDataDirectory_ResolvesFromContentRoot()
    {
        var options = new DatabaseOptions { DataDirectory = "Data" };

        var directory = options.ResolveDataDirectory(@"C:\app");

        Assert.Equal(Path.GetFullPath(Path.Combine(@"C:\app", "Data")), directory);
    }

    [Fact]
    public async Task Startup_CreatesDatabaseAndSeedsStation()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();

        var response = await client.GetAsync("/api/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(factory.DatabasePath));

        var station = Assert.Single(db.GamingStations);
        Assert.Equal(GamingStationSeeder.StationName, station.Name);
        Assert.Equal(GamingStationSeeder.StationCode, station.StationCode);
        Assert.Equal(TimeSpan.Zero, station.CreatedAt.Offset);
        Assert.NotEqual(Guid.Empty, station.Id);
        Assert.Empty(db.Database.GetPendingMigrations());
        Assert.Contains("InitialCreate", db.Database.GetAppliedMigrations().Single());
        Assert.Equal("wal", await ReadPragmaAsync(db, "journal_mode"));
    }

    [Fact]
    public void Restart_KeepsTheSeededStation()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "gamingstation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);

        try
        {
            Guid stationId;
            DateTimeOffset createdAt;

            using (var first = new GamingStationApiFactory(dataDirectory))
            {
                using var scope = first.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
                _ = first.CreateClient();
                var station = Assert.Single(db.GamingStations);
                stationId = station.Id;
                createdAt = station.CreatedAt;
            }

            SqliteConnection.ClearAllPools();

            using (var second = new GamingStationApiFactory(dataDirectory))
            {
                using var scope = second.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
                _ = second.CreateClient();
                var station = Assert.Single(db.GamingStations);
                Assert.Equal(stationId, station.Id);
                Assert.Equal(createdAt, station.CreatedAt);
                Assert.Equal(GamingStationSeeder.StationCode, station.StationCode);
                Assert.Empty(db.Sessions);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Session_BelongsToItsGamingStation()
    {
        using var factory = new GamingStationApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow();
        var station = db.GamingStations.Single();

        var session = new Session
        {
            Id = Guid.NewGuid(),
            GamingStationId = station.Id,
            StartTimeUtc = now,
            ExpiryTimeUtc = now.AddSeconds(120),
            DurationSeconds = 120,
            State = SessionState.Running,
            CreatedAt = now
        };
        db.Sessions.Add(session);
        db.SaveChanges();

        var loaded = db.GamingStations.Include(item => item.Sessions).Single();
        var stored = Assert.Single(loaded.Sessions);
        Assert.Equal(session.Id, stored.Id);
        Assert.Equal(station.Id, stored.GamingStationId);
        Assert.Equal(SessionState.Running, stored.State);

        db.ChangeTracker.Clear();
        db.Sessions.Add(new Session
        {
            Id = Guid.NewGuid(),
            GamingStationId = Guid.NewGuid(),
            StartTimeUtc = now,
            ExpiryTimeUtc = now.AddSeconds(120),
            DurationSeconds = 120,
            State = SessionState.Running,
            CreatedAt = now
        });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void OnlyOneOpenSession_IsAllowedPerStation()
    {
        using var factory = new GamingStationApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamingStationDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var station = db.GamingStations.Single();

        db.Sessions.Add(CreateSession(station.Id, now, SessionState.Closed));
        db.Sessions.Add(CreateSession(station.Id, now, SessionState.Closed));
        db.SaveChanges();

        db.Sessions.Add(CreateSession(station.Id, now, SessionState.Running));
        db.SaveChanges();

        db.ChangeTracker.Clear();
        db.Sessions.Add(CreateSession(station.Id, now, SessionState.AwaitingPayment));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    private static Session CreateSession(Guid stationId, DateTimeOffset now, SessionState state)
    {
        return new Session
        {
            Id = Guid.NewGuid(),
            GamingStationId = stationId,
            StartTimeUtc = now,
            ExpiryTimeUtc = now.AddSeconds(120),
            DurationSeconds = 120,
            State = state,
            CreatedAt = now
        };
    }

    private static async Task<string> ReadPragmaAsync(GamingStationDbContext db, string name)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        var value = await command.ExecuteScalarAsync();
        return Assert.IsType<string>(value);
    }
}
