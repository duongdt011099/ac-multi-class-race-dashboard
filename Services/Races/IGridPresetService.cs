using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Races;

/// <summary>Exporting a race's entered cars as a Content Manager grid preset.</summary>
public interface IGridPresetService
{
    Task<PresetExportResult> ExportGridPresetAsync(Guid raceId, SessionType sessionType, bool humanClassOnly, string seasonName, string championshipName);
    Task<PresetExportResult> ExportRaceGridPresetAsync(Guid raceId, string seasonName, string championshipName);
}
