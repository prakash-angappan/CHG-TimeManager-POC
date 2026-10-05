using Microsoft.EntityFrameworkCore;

namespace GamingStation.Api.Data;

public sealed class GamingStationDbContext : DbContext
{
    public GamingStationDbContext(DbContextOptions<GamingStationDbContext> options)
        : base(options)
    {
    }

    public DbSet<GamingStation> GamingStations => Set<GamingStation>();

    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GamingStation>(entity =>
        {
            entity.Property(station => station.Name).HasMaxLength(100).IsRequired();
            entity.Property(station => station.StationCode).HasMaxLength(64).IsRequired();
            entity.HasIndex(station => station.StationCode).IsUnique();
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.Property(session => session.State)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entity.HasOne(session => session.GamingStation)
                .WithMany(station => station.Sessions)
                .HasForeignKey(session => session.GamingStationId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            entity.HasIndex(session => session.GamingStationId)
                .IsUnique()
                .HasFilter("\"State\" <> 'Closed'")
                .HasDatabaseName("IX_Sessions_OneOpenPerStation");
        });
    }
}
