using System.Net;

namespace GamingStation.Api.Tests;

public class StaffPageTests
{
    [Theory]
    [InlineData("/staff")]
    [InlineData("/staff/")]
    public async Task StaffDashboard_IsServedByTheApi(string path)
    {
        using var factory = new GamingStationApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("id=\"root\"", html, StringComparison.Ordinal);
        Assert.Contains("/staff/assets/", html, StringComparison.Ordinal);
    }
}
