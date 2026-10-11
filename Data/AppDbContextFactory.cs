using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MulticlassRace.Data;

/// <summary>
/// Lets the EF tooling build a context without running the app, which the single-instance
/// guard in Program.cs would otherwise cut short before the host ever builds.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=app.db";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(DependencyInjection.ResolveForDesignTime(connectionString, basePath))
            .Options;

        return new AppDbContext(options);
    }
}