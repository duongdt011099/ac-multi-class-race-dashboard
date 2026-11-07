using MulticlassRace.Mapping;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Championships;

public class AchievementService : IAchievementService
{
    private readonly IAchievementRepository _achievementRepository;

    public AchievementService(IAchievementRepository achievementRepository)
    {
        _achievementRepository = achievementRepository;
    }

    public async Task<IReadOnlyList<AchievementModel>> GetForDriverAsync(Guid driverId)
    {
        var achievements = await _achievementRepository.GetByDriverAsync(driverId);

        return achievements
            .Select(a => a.ToModel())
            .OrderBy(a => a.IsSeasonTrophy ? 0 : 1)
            .ThenBy(a => a.Position)
            .ThenByDescending(a => a.EarnedAt)
            .ToList();
    }
}
