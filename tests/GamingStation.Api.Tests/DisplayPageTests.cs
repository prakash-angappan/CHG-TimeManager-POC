using System.Net;

namespace GamingStation.Api.Tests;

public class DisplayPageTests
{
    [Theory]
    [InlineData("/display/ps5-01")]
    [InlineData("/display/PS5-01")]
    [InlineData("/display/xbox-01")]
    public async Task DisplayPage_IsServedForAnyStationCode(string path)
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("id=\"station\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"connection\"", html, StringComparison.Ordinal);
        Assert.Contains("RECONNECTING", html, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden", html, StringComparison.Ordinal);
        Assert.Contains("/js/station-display.js", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Start session", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<input", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("READY TO RESUME", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisplayScript_ReadsRestThenSignalRAndDoesNotSendCommands()
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/station-display.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GAME RUNNING", script, StringComparison.Ordinal);
        Assert.Contains("TIME EXPIRED", script, StringComparison.Ordinal);
        Assert.Contains("AVAILABLE", script, StringComparison.Ordinal);
        Assert.DoesNotContain("READY TO RESUME", script, StringComparison.Ordinal);
        Assert.Contains("CONNECTED", script, StringComparison.Ordinal);
        Assert.Contains("RECONNECTING", script, StringComparison.Ordinal);
        Assert.Contains("CONNECTION LOST", script, StringComparison.Ordinal);
        Assert.Contains("station.name", script, StringComparison.Ordinal);
        Assert.Contains("stationName", script, StringComparison.Ordinal);
        Assert.Contains("setConnectionStatus(\"reconnecting\")", script, StringComparison.Ordinal);
        Assert.Contains("setConnectionStatus(\"lost\")", script, StringComparison.Ordinal);
        Assert.Contains("new AbortController()", script, StringComparison.Ordinal);
        Assert.Contains("controller.abort()", script, StringComparison.Ordinal);
        Assert.Contains("}, 5000)", script, StringComparison.Ordinal);
        Assert.Contains("controller.abort(), 2000)", script, StringComparison.Ordinal);
        Assert.Contains("await connection.stop()", script, StringComparison.Ordinal);
        Assert.Contains("stopHealthProbe", script, StringComparison.Ordinal);
        Assert.Contains("/api/health", script, StringComparison.Ordinal);
        Assert.Contains("/api/stations", script, StringComparison.Ordinal);
        Assert.Contains("/hubs/stations", script, StringComparison.Ordinal);
        Assert.Contains("StationStateChanged", script, StringComparison.Ordinal);
        Assert.Contains("JoinStation", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/sessions", script, StringComparison.Ordinal);
        Assert.Contains("await syncClock()", script, StringComparison.Ordinal);
        var clockIndex = script.IndexOf("await syncClock()", StringComparison.Ordinal);
        var stationIndex = script.IndexOf("await loadStation()", StringComparison.Ordinal);
        var startIndex = script.IndexOf("await connection.start()", StringComparison.Ordinal);
        var joinIndex = script.IndexOf("JoinStation", StringComparison.Ordinal);
        Assert.True(clockIndex >= 0 && clockIndex < stationIndex);
        Assert.True(stationIndex < startIndex);
        Assert.True(startIndex < joinIndex);
    }
}
