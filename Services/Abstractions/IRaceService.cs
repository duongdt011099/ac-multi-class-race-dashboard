using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Abstractions;

public interface IRaceService
{
    Task<IEnumerable<RaceModel>> GetRacesBySeasonAsync(Guid seasonId);
    Task<RaceModel?> GetRaceByIdAsync(Guid raceId);
    Task CreateRaceAsync(RaceFormModel model);
    Task UpdateRaceAsync(RaceFormModel model);
    Task DeleteRaceAsync(Guid raceId);
    Task LaunchSessionAsync(Guid raceId, SessionType sessionType, string weather);
    Task<IEnumerable<SessionModel>> GetRaceSessionsAsync(Guid raceId);
    Task<IEnumerable<TeamModel>> GetRaceTeamsAsync(Guid raceId);
    Task<int> GetEnteredCarCountAsync(Guid raceId);
    Task SaveRaceTeamsAsync(Guid raceId, IReadOnlyCollection<Guid> teamIds);
    Task<PresetExportResult> ExportGridPresetAsync(Guid raceId, SessionType sessionType, bool humanClassOnly, string seasonName, string championshipName);
    Task<PresetExportResult> ExportRaceGridPresetAsync(Guid raceId, string seasonName, string championshipName);
    Task<IEnumerable<RaceResultFileModel>> GetRaceResultFilesAsync();
    Task<RaceResultImportResult> ImportRaceResultAsync(Guid raceId, string fileName);
    Task<RaceResultImportResult> ImportSessionResultAsync(Guid raceId, SessionType sessionType, Stream stream, string? fileName);
}