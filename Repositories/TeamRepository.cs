using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class TeamRepository : GenericRepository<Team>, ITeamRepository
{
    private readonly ILogger<TeamRepository> _logger;

    public TeamRepository(AppDbContext context, ILogger<TeamRepository> logger) : base(context)
    {
        _logger = logger;
    }

    public async Task DeacivateTeamAsync(Guid teamId)
    {
        var team = await _context.Teams.FirstOrDefaultAsync(t => t.TeamId == teamId);

        if (team is not null)
        {
            team.IsActive = false;
            await _context.SaveChangesAsync();

            return;
        }

        _logger.LogWarning("Team with ID {TeamId} not found for deactivation.", teamId);
    }

    public async Task<IEnumerable<Team>> GetActiveTeamsAsync()
    {
        return await _context.Teams
        .Include(team => team.ClassSettingItems)
            .ThenInclude(item => item.ClassSetting)
        .Where(t => t.IsActive)
        .ToListAsync();
    }

    public async Task<Team?> GetTeamDetailsAsync(Guid teamId)
    {
        return await _context.Teams
            .Include(t => t.ClassSettingItems)
                .ThenInclude(item => item.ClassSetting)
            .Include(t => t.Drivers)
            .FirstOrDefaultAsync(t => t.TeamId == teamId);
    }

    public async Task CreateTeamAsync(Team team, IReadOnlyCollection<Guid> classItemIds, IReadOnlyCollection<Guid> driverIds)
    {
        var classItems = await ResolveClassItemsAsync(classItemIds);

        foreach (var item in classItems)
        {
            team.ClassSettingItems.Add(item);
        }

        var requestedIds = driverIds.ToList();
        var drivers = await _context.Driver 
            .Where(d => requestedIds.Contains(d.DriverId) && d.Team == null)
            .ToListAsync();

        foreach (var driver in drivers)
        {
            driver.Team = team;
            team.Drivers.Add(driver);
        }

        await _context.Teams.AddAsync(team);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateTeamAsync(Team team, IReadOnlyCollection<Guid> classItemIds, IReadOnlyCollection<Guid> driverIds)
    {
        var existing = await _context.Teams
            .Include(t => t.ClassSettingItems)
            .Include(t => t.Drivers)
            .FirstOrDefaultAsync(t => t.TeamId == team.TeamId);

        if (existing is null)
        {
            _logger.LogWarning("Team with ID {TeamId} not found for update.", team.TeamId);
            return;
        }

        var classItems = await ResolveClassItemsAsync(classItemIds);

        existing.TeamName = team.TeamName;
        existing.TeamLogo = team.TeamLogo;
        existing.IsActive = team.IsActive;

        existing.ClassSettingItems.Clear();

        foreach (var item in classItems)
        {
            existing.ClassSettingItems.Add(item);
        }

        var requestedIds = driverIds.ToList();
        var currentIds = existing.Drivers.Select(d => d.DriverId).ToHashSet();

        foreach (var driver in existing.Drivers.Where(d => !requestedIds.Contains(d.DriverId)).ToList())
        {
            existing.Drivers.Remove(driver);
            driver.Team = null;
        }

        var toAdd = await _context.Driver
            .Where(d => requestedIds.Contains(d.DriverId) && !currentIds.Contains(d.DriverId) && d.Team == null)
            .ToListAsync();

        foreach (var driver in toAdd)
        {
            driver.Team = existing;
            existing.Drivers.Add(driver);
        }

        await _context.SaveChangesAsync();
    }

    private async Task<List<ClassSettingItem>> ResolveClassItemsAsync(IEnumerable<Guid> itemIds)
    {
        var requested = itemIds.Distinct().ToList();

        if (requested.Count == 0)
        {
            return new List<ClassSettingItem>();
        }

        var items = await _context.ClassSettingItems
            .Include(i => i.ClassSetting)
            .Where(i => requested.Contains(i.ClassSettingItemId))
            .ToListAsync();

        var found = items.Select(i => i.ClassSettingItemId).ToHashSet();

        if (found.Count != requested.Count)
        {
            throw new InvalidOperationException("One or more selected class items no longer exist.");
        }

        var duplicateSetting = items
            .GroupBy(i => i.ClassSettingId)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateSetting is not null)
        {
            var settingName = duplicateSetting.First().ClassSetting.ClassSettingName;
            throw new InvalidOperationException(
                $"A team can only have one class from the same setting ('{settingName}').");
        }

        return items
            .OrderBy(i => i.ClassSetting.ClassSettingName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Order)
            .ToList();
    }
}