using Microsoft.EntityFrameworkCore;
using MulticlassRace.Models;

namespace MulticlassRace.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Championship> Championships => Set<Championship>();

    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<Race> Races => Set<Race>();

    public DbSet<ClassSetting> ClassSettings => Set<ClassSetting>();

    public DbSet<ClassSettingItem> ClassSettingItems => Set<ClassSettingItem>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<Driver> Driver => Set<Driver>();

    public DbSet<DriverStanding> DriverStandings => Set<DriverStanding>();

    public DbSet<AssettoCorsaGameConfig> AssettoCorsaGameConfigs => Set<AssettoCorsaGameConfig>();

    public DbSet<PointSetting> PointSettings => Set<PointSetting>();

    public DbSet<AppUpdateState> AppUpdateStates => Set<AppUpdateState>();

    public DbSet<ImportedLuaResult> ImportedLuaResults => Set<ImportedLuaResult>();

    /// <summary>Relationships, indexes, converters and seed data live in <c>Data/Configurations</c>, one class per entity.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
