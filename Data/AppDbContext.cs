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

    public DbSet<TeamClass> TeamClasses => Set<TeamClass>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<Driver> Driver => Set<Driver>();

    public DbSet<DriverStanding> DriverStandings => Set<DriverStanding>();

    public DbSet<AssettoCorsaGameConfig> AssettoCorsaGameConfigs => Set<AssettoCorsaGameConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Race>()
            .HasMany(r => r.EnteredTeams)
            .WithMany();

        modelBuilder.Entity<TeamClass>().HasData(
            new TeamClass { TeamClassId = new Guid("2254e881-f300-4154-a84f-600196de6081"), TeamClassName = "Hypercar", IsActive = true },
            new TeamClass { TeamClassId = new Guid("2254e881-f300-4154-a84f-600196de6082"), TeamClassName = "LMP2", IsActive = true },
            new TeamClass { TeamClassId = new Guid("2254e881-f300-4154-a84f-600196de6083"), TeamClassName = "GT3", IsActive = true });
    }
}