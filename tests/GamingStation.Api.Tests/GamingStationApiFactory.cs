using GamingStation.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace GamingStation.Api.Tests;

public sealed class GamingStationApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _deleteOnDispose;

    public GamingStationApiFactory()
        : this(CreateTemporaryDirectory(), deleteOnDispose: true)
    {
    }

    internal GamingStationApiFactory(string dataDirectory)
        : this(dataDirectory, deleteOnDispose: false)
    {
    }

    private GamingStationApiFactory(string dataDirectory, bool deleteOnDispose)
    {
        DataDirectory = dataDirectory;
        _deleteOnDispose = deleteOnDispose;
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }

    public string DatabasePath => Path.Combine(DataDirectory, DatabaseOptions.DatabaseFileName);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DataDirectory"] = DataDirectory
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || !_deleteOnDispose)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "gamingstation-tests", Guid.NewGuid().ToString("N"));
    }
}
