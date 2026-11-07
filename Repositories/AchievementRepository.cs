using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class AchievementRepository : GenericRepository<Achievement>, IAchievementRepository
{
    public AchievementRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Achievement>> GetByDriverAsync(Guid driverId)
    {
        return await _context.Achievements
            .Where(a => a.DriverId == driverId)
            .OrderByDescending(a => a.EarnedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Achievement>> GetBySeasonAsync(Guid seasonId, bool seasonTrophiesOnly)
    {
        var query = _context.Achievements.Where(a => a.SeasonId == seasonId);

        if (seasonTrophiesOnly)
        {
            query = query.Where(a => a.Type != AchievementType.RaceWin);
        }

        return await query.ToListAsync();
    }

    public async Task<IEnumerable<Achievement>> GetByRaceAsync(Guid raceId)
    {
        return await _context.Achievements
            .Where(a => a.RaceId == raceId)
            .ToListAsync();
    }

    public async Task RemoveRangeAsync(IEnumerable<Achievement> achievements)
    {
        _context.Achievements.RemoveRange(achievements);
        await _context.SaveChangesAsync();
    }
}
