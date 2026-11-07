using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class SeasonRepository : GenericRepository<Season>, ISeasonRepository
{
    public SeasonRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Season>> GetSeasonsByChampionshipAsync(Guid championshipId)
    {
        return await _context.Seasons
            .Include(s => s.Races)
            .Include(s => s.ClassSetting)
            .Where(s => s.Championship.ChampionshipId == championshipId)
            .OrderBy(s => s.SeasonName)
            .ToListAsync();
    }

    /// <summary>The season with everything needed to total its championship: races, their sessions and driver standings.</summary>
    public async Task<Season?> GetSeasonForCompletionAsync(Guid seasonId)
    {
        return await _context.Seasons
            .Include(s => s.Championship)
            .Include(s => s.Races)
                .ThenInclude(r => r.Sessions)
                .ThenInclude(s => s.DriverStandings)
                .ThenInclude(d => d.Driver)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.SeasonId == seasonId);
    }

    public async Task<Season?> GetSeasonWithRacesAsync(Guid seasonId)
    {
        return await _context.Seasons
            .Include(s => s.Championship)
            .Include(s => s.Races)
                .ThenInclude(r => r.PointSetting)
            .Include(s => s.Races)
                .ThenInclude(r => r.ClassSetting)
            .FirstOrDefaultAsync(s => s.SeasonId == seasonId);
    }
}