using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Races;

/// <summary>Finding and importing session results written by Content Manager or the Lua app.</summary>
public interface IRaceResultService
{
    Task<IEnumerable<RaceResultFileModel>> GetRaceResultFilesAsync();
    Task<ResultImportAvailabilityModel> GetResultImportAvailabilityAsync();
    Task<RaceResultImportResult> ImportRaceResultAsync(Guid raceId, string fileName);
    Task<RaceResultImportResult> ImportRaceResultFromPathAsync(Guid raceId, string fullPath, string? fileName);
    Task<RaceResultImportResult> ImportSessionResultAsync(Guid raceId, SessionType sessionType, Stream stream, string? fileName);
}
