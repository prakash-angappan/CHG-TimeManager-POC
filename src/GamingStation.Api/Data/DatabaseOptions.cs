namespace GamingStation.Api.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string DatabaseFileName = "gamingstation.db";

    /// <summary>
    /// Directory that holds the SQLite file. Empty uses
    /// %LOCALAPPDATA%\GamingStation, outside the source tree.
    /// A relative path is resolved from the application content root.
    /// </summary>
    public string? DataDirectory { get; set; }

    public string ResolveDataDirectory(string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(DataDirectory))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "GamingStation");
        }

        return Path.IsPathRooted(DataDirectory)
            ? Path.GetFullPath(DataDirectory)
            : Path.GetFullPath(Path.Combine(contentRootPath, DataDirectory));
    }

    public string GetDatabasePath(string contentRootPath)
    {
        return Path.Combine(ResolveDataDirectory(contentRootPath), DatabaseFileName);
    }
}
