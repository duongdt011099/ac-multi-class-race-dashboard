using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class ChampionshipRepository : GenericRepository<Championship>, IChampionshipRepository
{
    private readonly ILogger<ChampionshipRepository> _logger;

    public ChampionshipRepository(AppDbContext context, ILogger<ChampionshipRepository> logger) : base(context)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<Championship>> GetActiveChampionshipsAsync()
    {
        return await _context.Championships
            .Include(c => c.Seasons)
            .Where(c => c.IsActive)
            .OrderBy(c => c.ChampionshipName)
            .ToListAsync();
    }

    public async Task DeacivateChampionshipAsync(Guid championshipId)
    {
        var championship = await _context.Championships.FirstOrDefaultAsync(c => c.ChampionshipId == championshipId);

        if (championship is not null)
        {
            championship.IsActive = false;
            await _context.SaveChangesAsync();

            return;
        }

        _logger.LogWarning("Championship with ID {ChampionshipId} not found for deactivation.", championshipId);
    }
}