using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface IAchievementRepository : IGenericRepository<Achievement>
{
    Task<IEnumerable<Achievement>> GetByDriverAsync(Guid driverId);
    Task<IEnumerable<Achievement>> GetBySeasonAsync(Guid seasonId, bool seasonTrophiesOnly);
    Task<IEnumerable<Achievement>> GetByRaceAsync(Guid raceId);
    Task RemoveRangeAsync(IEnumerable<Achievement> achievements);
}
