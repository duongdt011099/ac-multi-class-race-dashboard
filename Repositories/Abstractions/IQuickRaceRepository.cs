using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface IQuickRaceRepository
{
    Task<QuickRaceSetup?> GetSetupAsync();

    /// <summary>Saves the setup document, creating the single setup row on first use.</summary>
    Task SaveSetupJsonAsync(string setupJson);

    Task SaveLaunchJsonAsync(string launchJson);

    Task<List<QuickRaceResult>> GetResultsAsync();

    Task<QuickRaceResult?> GetResultAsync(Guid resultId);

    Task AddResultAsync(QuickRaceResult result);

    Task DeleteResultAsync(Guid resultId);
}
