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
        .Include(team => team.TeamClass)
        .Where(t => t.IsActive)
        .ToListAsync();
    }

    public async Task<Team?> GetTeamDetailsAsync(Guid teamId)
    {
        return await _context.Teams
            .Include(t => t.TeamClass)
            .Include(t => t.Drivers)
            .FirstOrDefaultAsync(t => t.TeamId == teamId);
    }

    public async Task CreateTeamAsync(Team team, IReadOnlyCollection<Guid> driverIds)
    {
        var teamClass = (await _context.TeamClasses
            .ToListAsync())
            .FirstOrDefault(tc => tc.TeamClassId == team.TeamClass.TeamClassId);

        if (teamClass is null)
        {
            throw new InvalidOperationException($"Team class with ID {team.TeamClass.TeamClassId} not found.");
        }

        team.TeamClass = teamClass;

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

    public async Task UpdateTeamAsync(Team team, IReadOnlyCollection<Guid> driverIds)
    {
        var existing = await _context.Teams
            .Include(t => t.TeamClass)
            .Include(t => t.Drivers)
            .FirstOrDefaultAsync(t => t.TeamId == team.TeamId);

        if (existing is null)
        {
            _logger.LogWarning("Team with ID {TeamId} not found for update.", team.TeamId);
            return;
        }

        var teamClass = await _context.TeamClasses
            .FirstOrDefaultAsync(tc => tc.TeamClassId == team.TeamClass.TeamClassId);

        if (teamClass is null)
        {
            throw new InvalidOperationException($"Team class with ID {team.TeamClass.TeamClassId} not found.");
        }

        existing.TeamName = team.TeamName;
        existing.TeamLogo = team.TeamLogo;
        existing.TeamClass = teamClass;
        existing.IsActive = team.IsActive;

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
}
