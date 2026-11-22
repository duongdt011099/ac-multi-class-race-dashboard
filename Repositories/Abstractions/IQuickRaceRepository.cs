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

    /// <summary>Saved grids, by name.</summary>
    Task<List<QuickRacePreset>> GetPresetsAsync();

    Task<QuickRacePreset?> GetPresetAsync(Guid presetId);

    /// <summary>Adds the preset, or replaces the name and grid of the one with the same id.</summary>
    Task SavePresetAsync(QuickRacePreset preset);

    Task DeletePresetAsync(Guid presetId);
}
