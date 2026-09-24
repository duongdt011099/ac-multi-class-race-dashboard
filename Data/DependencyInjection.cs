using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace MulticlassRace.Data;

public static class DependencyInjection
{
    public static IServiceCollection RegisterDbContext(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
            options.UseSqlite(ResolveDataSource(connectionString, contentRootPath));
        });

        return services;
    }

    private static string ResolveDataSource(string connectionString, string contentRootPath)
    {
        var match = Regex.Match(connectionString, @"(?i)(Data\s*Source\s*=)\s*(.+?)(;|$)");
        if (!match.Success)
        {
            return connectionString;
        }

        var value = match.Groups[2].Value.Trim().Trim('"');
        if (Path.IsPathRooted(value))
        {
            return connectionString;
        }

        var absolute = Path.GetFullPath(Path.Combine(contentRootPath, value));
        return connectionString[..match.Groups[1].Index]
               + match.Groups[1].Value
               + $" \"{absolute}\""
               + connectionString[match.Groups[3].Index..];
    }
}