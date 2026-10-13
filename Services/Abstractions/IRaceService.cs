using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Abstractions;

/// <summary>Races: listing, editing, entered teams and the sessions recorded for them.</summary>
public interface IRaceService
{
    Task<IEnumerable<RaceModel>> GetRacesBySeasonAsync(Guid seasonId);
    Task<RaceModel?> GetRaceByIdAsync(Guid raceId);
    Task<IEnumerable<RaceModel>> GetCandidateRacesAsync(string? trackName, SessionType sessionType);
    Task CreateRaceAsync(RaceFormModel model);
    Task UpdateRaceAsync(RaceFormModel model);
    Task DeleteRaceAsync(Guid raceId);
    Task<IEnumerable<SessionModel>> GetRaceSessionsAsync(Guid raceId);
    Task<IEnumerable<TeamModel>> GetRaceTeamsAsync(Guid raceId);
    Task<int> GetEnteredCarCountAsync(Guid raceId);
    Task SaveRaceTeamsAsync(Guid raceId, IReadOnlyCollection<Guid> teamIds);
}
