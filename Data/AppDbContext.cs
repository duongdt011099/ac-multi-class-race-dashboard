using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MulticlassRace.Models;

namespace MulticlassRace.Data;

public class AppDbContext : DbContext
{
    public const string DefaultClassSettingId = "2254e881-f300-4154-a84f-600196de6000";

    public const string DefaultHypercarItemId = "2254e881-f300-4154-a84f-600196de6081";

    public const string DefaultLmp2ItemId = "2254e881-f300-4154-a84f-600196de6082";

    public const string DefaultGt3ItemId = "2254e881-f300-4154-a84f-600196de6083";

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

        modelBuilder.Entity<Race>()
            .HasOne(r => r.ClassSetting)
            .WithMany(c => c.Races)
            .HasForeignKey(r => r.ClassSettingId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClassSetting>()
            .HasMany(c => c.Items)
            .WithOne(i => i.ClassSetting)
            .HasForeignKey(i => i.ClassSettingId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Team>()
            .HasMany(t => t.ClassSettingItems)
            .WithMany(i => i.Teams)
            .UsingEntity(j => j.ToTable("TeamClassSettingItem"));

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

        // Dedupe relies on one row per file, so the path is unique.
        modelBuilder.Entity<ImportedLuaResult>()
            .HasIndex(r => r.FullPath)
            .IsUnique();

        modelBuilder.Entity<ImportedLuaResult>()
            .HasIndex(r => new { r.FileName, r.SessionType })
                .HasDatabaseName("IX_ImportedLuaResults_FileName_SessionType");

        modelBuilder.Entity<ClassSetting>().HasData(
            new ClassSetting
            {
                ClassSettingId = new Guid(DefaultClassSettingId),
                ClassSettingName = "WEC",
                IsDefault = true
            });

        modelBuilder.Entity<ClassSettingItem>().HasData(
            new ClassSettingItem
            {
                ClassSettingItemId = new Guid(DefaultHypercarItemId),
                ClassSettingId = new Guid(DefaultClassSettingId),
                Name = "Hypercar",
                Order = 1,
                Color = "#e10600"
            },
            new ClassSettingItem
            {
                ClassSettingItemId = new Guid(DefaultLmp2ItemId),
                ClassSettingId = new Guid(DefaultClassSettingId),
                Name = "LMP2",
                Order = 2,
                Color = "#0057b8"
            },
            new ClassSettingItem
            {
                ClassSettingItemId = new Guid(DefaultGt3ItemId),
                ClassSettingId = new Guid(DefaultClassSettingId),
                Name = "GT3",
                Order = 3,
                Color = "#3fa34d"
            });

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