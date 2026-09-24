using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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

    public DbSet<TeamClass> TeamClasses => Set<TeamClass>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<Driver> Driver => Set<Driver>();

    public DbSet<DriverStanding> DriverStandings => Set<DriverStanding>();

    public DbSet<AssettoCorsaGameConfig> AssettoCorsaGameConfigs => Set<AssettoCorsaGameConfig>();

    public DbSet<PointSetting> PointSettings => Set<PointSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Race>()
            .HasMany(r => r.EnteredTeams)
            .WithMany();

        modelBuilder.Entity<Race>()
            .HasOne(r => r.PointSetting)
            .WithMany()
            .HasForeignKey(r => r.PointSettingId)
            .OnDelete(DeleteBehavior.Restrict);

        var configConverter = new ValueConverter<Dictionary<string, int>, string>(
            v => JsonSerializer.Serialize(v, new JsonSerializerOptions { WriteIndented = false }),
            v => string.IsNullOrWhiteSpace(v)
                ? new Dictionary<string, int>()
                : JsonSerializer.Deserialize<Dictionary<string, int>>(v) ?? new Dictionary<string, int>());

        var configComparer = new ValueComparer<Dictionary<string, int>>(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
            v => v.ToDictionary(kv => kv.Key, kv => kv.Value));

        modelBuilder.Entity<PointSetting>(entity =>
        {
            entity.HasKey(p => p.SettingId);

            entity.Property(p => p.Config)
                .HasConversion(configConverter)
                .Metadata.SetValueComparer(configComparer);
        });

        modelBuilder.Entity<TeamClass>().HasData(
            new TeamClass { TeamClassId = new Guid("2254e881-f300-4154-a84f-600196de6081"), TeamClassName = "Hypercar", IsActive = true },
            new TeamClass { TeamClassId = new Guid("2254e881-f300-4154-a84f-600196de6082"), TeamClassName = "LMP2", IsActive = true },
            new TeamClass { TeamClassId = new Guid("2254e881-f300-4154-a84f-600196de6083"), TeamClassName = "GT3", IsActive = true });

        modelBuilder.Entity<PointSetting>().HasData(
            new PointSetting
            {
                SettingId = new Guid("d8f10b2a-5a6b-4c7d-8e9f-0a1b2c3d4e5f"),
                SettingName = "Standard",
                IsDefault = true,
                Config = new Dictionary<string, int>
                {
                    ["1"] = 25,
                    ["2"] = 18,
                    ["3"] = 15,
                    ["4"] = 12,
                    ["5"] = 10,
                    ["6"] = 8,
                    ["7"] = 6,
                    ["8"] = 4,
                    ["9"] = 2,
                    ["10"] = 1
                }
            });
    }
}