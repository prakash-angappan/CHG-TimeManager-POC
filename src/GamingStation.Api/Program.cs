using System.Text.Json;
using System.Text.Json.Serialization;
using GamingStation.Api.Data;
using GamingStation.Api.Realtime;
using GamingStation.Api.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
}).AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services.AddOptions<SessionOptions>()
    .Bind(builder.Configuration.GetSection(SessionOptions.SectionName))
    .Validate(
        options => options.DefaultDurationSeconds > 0,
        "Session:DefaultDurationSeconds must be greater than zero.")
    .Validate(
        options => options.ExpiryCheckIntervalSeconds > 0,
        "Session:ExpiryCheckIntervalSeconds must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.AddSingleton<SqliteWalConnectionInterceptor>();
builder.Services.AddDbContext<GamingStationDbContext>((services, options) =>
{
    var environment = services.GetRequiredService<IHostEnvironment>();
    var databaseOptions = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    var databasePath = databaseOptions.GetDatabasePath(environment.ContentRootPath);
    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    options.UseSqlite($"Data Source={databasePath}");
    options.AddInterceptors(services.GetRequiredService<SqliteWalConnectionInterceptor>());
});
builder.Services.AddScoped<SessionLifecycle>();
builder.Services.AddScoped<IStationNotifier, SignalRStationNotifier>();
builder.Services.AddHostedService<SessionExpiryService>();

var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/staff", ServeStaff);

app.MapGet("/display/{stationCode}", (string stationCode) =>
{
    if (stationCode.Contains('.', StringComparison.Ordinal))
    {
        return Results.NotFound();
    }

    var displayPath = Path.Combine(app.Environment.WebRootPath, "display", "index.html");
    return Results.File(displayPath, "text/html; charset=utf-8");
});

app.MapGet("/api/health", ([FromServices] TimeProvider clock) =>
{
    var body = new HealthResponse("ok", clock.GetUtcNow());
    return Results.Ok(body);
});
StationSessionEndpoints.Map(app);
app.MapHub<StationHub>("/hubs/stations");

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var environment = services.GetRequiredService<IHostEnvironment>();
    var databaseOptions = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    var databasePath = databaseOptions.GetDatabasePath(environment.ContentRootPath);
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("GamingStation.Database");
    var db = services.GetRequiredService<GamingStationDbContext>();
    db.Database.Migrate();
    GamingStationSeeder.Seed(db, services.GetRequiredService<TimeProvider>());
    logger.LogInformation("SQLite database ready at {DatabasePath}", databasePath);
}

app.Run();

IResult ServeStaff()
{
    var staffPath = Path.Combine(app.Environment.WebRootPath, "staff", "index.html");
    return Results.File(staffPath, "text/html; charset=utf-8");
}

public sealed record HealthResponse(string Status, DateTimeOffset ServerTimeUtc);

public sealed class SessionOptions
{
    public const string SectionName = "Session";

    public int DefaultDurationSeconds { get; set; } = 120;

    public int ExpiryCheckIntervalSeconds { get; set; } = 1;
}

public partial class Program;
