using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class RaceRepository : GenericRepository<Race>, IRaceRepository
{
    public RaceRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Race>> GetRacesBySeasonAsync(Guid seasonId)
    {
        return await _context.Races
            .Include(r => r.Sessions)
                .ThenInclude(s => s.DriverStandings)
            .Where(r => r.Season.SeasonId == seasonId)
            .OrderBy(r => r.RaceName)
            .ToListAsync();
    }

    public async Task<IEnumerable<Session>> GetSessionsByRaceAsync(Guid raceId)
    {
        return await _context.Set<Session>()
            .Include(s => s.DriverStandings)
                .ThenInclude(d => d.Driver)
            .Where(s => s.RaceId == raceId)
            .OrderBy(s => s.SessionType)
            .ThenBy(s => s.SessionDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Team>> GetEnteredTeamsAsync(Guid raceId)
    {
        return await _context.Races
            .Where(r => r.RaceId == raceId)
            .SelectMany(r => r.EnteredTeams)
            .Include(t => t.TeamClass)
            .OrderBy(t => t.TeamName)
            .ToListAsync();
    }

    public async Task<IEnumerable<Team>> GetEnteredTeamsWithDriversAsync(Guid raceId)
    {
        return await _context.Races
            .Where(r => r.RaceId == raceId)
            .SelectMany(r => r.EnteredTeams)
            .Include(t => t.TeamClass)
            .Include(t => t.Drivers)
            .OrderBy(t => t.TeamName)
            .ToListAsync();
    }

    public async Task UpdateRaceTeamsAsync(Guid raceId, IReadOnlyCollection<Guid> teamIds)
    {
        var race = await _context.Races
            .Include(r => r.EnteredTeams)
            .FirstOrDefaultAsync(r => r.RaceId == raceId);

        if (race is null)
        {
            return;
        }

        var requestedIds = teamIds.ToList();
        var currentIds = race.EnteredTeams.Select(t => t.TeamId).ToHashSet();

        foreach (var team in race.EnteredTeams.Where(t => !requestedIds.Contains(t.TeamId)).ToList())
        {
            race.EnteredTeams.Remove(team);
        }

        var toAdd = requestedIds.Where(id => !currentIds.Contains(id)).ToList();

        if (toAdd.Count > 0)
        {
            var teams = await _context.Teams
                .Where(t => toAdd.Contains(t.TeamId))
                .ToListAsync();

            foreach (var team in teams)
            {
                race.EnteredTeams.Add(team);
            }
        }

        await _context.SaveChangesAsync();
    }
}