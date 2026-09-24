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
}