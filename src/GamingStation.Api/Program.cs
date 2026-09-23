using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<SessionOptions>()
    .Bind(builder.Configuration.GetSection(SessionOptions.SectionName))
    .Validate(
        options => options.DefaultDurationSeconds > 0,
        "Session:DefaultDurationSeconds must be greater than zero.")
    .ValidateOnStart();

var app = builder.Build();

app.MapGet("/api/health", ([FromServices] TimeProvider clock) =>
{
    var body = new HealthResponse("ok", clock.GetUtcNow());
    return Results.Ok(body);
});

app.Run();

public sealed record HealthResponse(string Status, DateTimeOffset ServerTimeUtc);

public sealed class SessionOptions
{
    public const string SectionName = "Session";

    public int DefaultDurationSeconds { get; set; } = 120;
}

public partial class Program;
