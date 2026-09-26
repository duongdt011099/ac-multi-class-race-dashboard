using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class DriverRepository : GenericRepository<Driver>, IDriverRepository
{
    private readonly ILogger<DriverRepository> _logger;

    public DriverRepository(AppDbContext context, ILogger<DriverRepository> logger) : base(context)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<Driver>> GetActiveDriversAsync()
    {
        return await _context.Driver
            .Include(d => d.Team)
            .ThenInclude(t => t!.TeamClass)
            .Where(d => d.IsActive)
            .ToListAsync();
    }

    public async Task<IEnumerable<Driver>> GetDriversByTeamAsync(Guid teamId)
    {
        return await _context.Driver
            .Where(d => d.Team != null && d.Team.TeamId == teamId)
            .ToListAsync();
    }

    public async Task DeacivateDriverAsync(Guid driverId)
    {
        var driver = await _context.Driver.FirstOrDefaultAsync(d => d.DriverId == driverId);

        if (driver is not null)
        {
            driver.IsActive = false;
            await _context.SaveChangesAsync();

            return;
        }

        _logger.LogWarning("Driver with ID {DriverId} not found for deactivation.", driverId);
    }

    public async Task<IEnumerable<Driver>> SearchUnassignedDriversAsync(string? searchTerm, int limit = 10)
    {
        var query = _context.Driver.Where(d => d.IsActive && d.Team == null);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            query = query.Where(d => d.DriverName.Contains(term));
        }

        return await query
            .OrderBy(d => d.DriverName)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> AddMissingAsync(IEnumerable<Driver> candidates)
    {
        var existingKeys = new HashSet<string>(
            await _context.Driver
                .Select(d => d.Car + "\u001F" + d.Skin)
                .ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        var toAdd = candidates
            .Where(d => !existingKeys.Contains(d.Car + "\u001F" + d.Skin))
            .ToList();

        if (toAdd.Count > 0)
        {
            await _context.Driver.AddRangeAsync(toAdd);
            await _context.SaveChangesAsync();
        }

        return toAdd.Count;
    }

    public async Task UnsetHumanFlagAsync(Guid? exceptDriverId = null)
    {
        await _context.Driver
            .Where(d => d.IsHuman && (exceptDriverId == null || d.DriverId != exceptDriverId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsHuman, false));
    }
}
