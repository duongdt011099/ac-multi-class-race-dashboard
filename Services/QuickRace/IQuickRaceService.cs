using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.QuickRace;

/// <summary>Quick Race: a one-off race with its own grid, no championship, classes or points.</summary>
public interface IQuickRaceService
{
    Task<QuickRaceSetupModel> GetSetupAsync();

    Task SaveSetupAsync(QuickRaceSetupModel setup);

    /// <summary>Finished quick races, newest first.</summary>
    Task<IReadOnlyList<QuickRaceHistoryItem>> GetHistoryAsync();

    Task<QuickRaceResultModel?> GetResultAsync(Guid resultId);

    Task DeleteResultAsync(Guid resultId);

    /// <summary>Saved grids, by name.</summary>
    Task<IReadOnlyList<QuickRacePresetItem>> GetPresetsAsync();

    /// <summary>The saved grid of a preset; null when it was deleted.</summary>
    Task<QuickRacePresetGrid?> GetPresetGridAsync(Guid presetId);

    /// <summary>
    /// Saves the grid of <paramref name="setup"/> as a preset: over the preset <paramref name="overwriteId"/>
    /// when given, otherwise as a new one. The name must be unique among the other presets.
    /// </summary>
    Task<QuickRacePresetItem> SavePresetAsync(string name, QuickRaceSetupModel setup, Guid? overwriteId = null);

    Task DeletePresetAsync(Guid presetId);

    /// <summary>
    /// Reads a race result file written by the Lua app (or Content Manager) and stores it in the history.
    /// The cars are matched against the grid that was last launched.
    /// </summary>
    Task<QuickRaceResultModel> ImportResultFromPathAsync(string fullPath, string? fileName);

    /// <summary>
    /// Reads a qualifying result and puts the cars of the saved grid in qualifying order. Returns the
    /// qualifying order, so the page can celebrate a pole position.
    /// </summary>
    Task<QuickRaceQualifyingModel> ImportQualifyingFromPathAsync(string fullPath, string? fileName);
}

public interface IQuickRaceLauncher
{
    /// <summary>Builds race.ini and the session file from the saved quick race setup and starts Assetto Corsa.</summary>
    Task LaunchAsync(SessionType sessionType = SessionType.Race);
}
