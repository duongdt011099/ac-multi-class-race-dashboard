using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Championships;

public interface IAchievementService
{
    /// <summary>The trophies of one driver, season trophies first (best first), then race wins, newest first.</summary>
    Task<IReadOnlyList<AchievementModel>> GetForDriverAsync(Guid driverId);
}
