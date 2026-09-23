using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GamingStation.Api.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_ReturnsOkStatusAndUtcTimestamp()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var names = root.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();

        Assert.Equal(["serverTimeUtc", "status"], names);
        Assert.Equal("ok", root.GetProperty("status").GetString());

        var serverTime = root.GetProperty("serverTimeUtc").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, serverTime.Offset);
        Assert.True((DateTimeOffset.UtcNow - serverTime).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetUnknownPath_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void SessionDuration_UsesConfiguredValue()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Session:DefaultDurationSeconds"] = "45"
                });
            });
        });

        var options = factory.Services.GetRequiredService<IOptions<SessionOptions>>().Value;

        Assert.Equal(45, options.DefaultDurationSeconds);
    }

    [Fact]
    public void SessionDuration_DefaultsTo120Seconds()
    {
        var options = _factory.Services.GetRequiredService<IOptions<SessionOptions>>().Value;

        Assert.Equal(120, options.DefaultDurationSeconds);
    }

    [Fact]
    public void ListenUrl_IsConfiguredForEveryInterface()
    {
        var urls = _factory.Services.GetRequiredService<IConfiguration>()["Urls"];

        Assert.Equal("http://0.0.0.0:5080", urls);
    }

    [Fact]
    public void NonPositiveSessionDuration_PreventsStartup()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Session:DefaultDurationSeconds"] = "0"
                });
            });
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("DefaultDurationSeconds", exception.ToString(), StringComparison.Ordinal);
    }
}
