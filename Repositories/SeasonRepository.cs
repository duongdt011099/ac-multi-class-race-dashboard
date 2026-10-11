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
            .Where(s => s.Championship.ChampionshipId == championshipId)
            .OrderBy(s => s.SeasonName)
            .ToListAsync();
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