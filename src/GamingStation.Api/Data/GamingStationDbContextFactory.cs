using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GamingStation.Api.Data;

public sealed class GamingStationDbContextFactory : IDesignTimeDbContextFactory<GamingStationDbContext>
{
    public GamingStationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GamingStationDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        return new GamingStationDbContext(options);
    }
}
