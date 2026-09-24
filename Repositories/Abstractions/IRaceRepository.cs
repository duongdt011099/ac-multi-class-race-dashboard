using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface IRaceRepository : IGenericRepository<Race>
{
    Task<IEnumerable<Race>> GetRacesBySeasonAsync(Guid seasonId);
    Task<IEnumerable<Session>> GetSessionsByRaceAsync(Guid raceId);
    Task<IEnumerable<Team>> GetEnteredTeamsAsync(Guid raceId);
    Task<IEnumerable<Team>> GetEnteredTeamsWithDriversAsync(Guid raceId);
    Task UpdateRaceTeamsAsync(Guid raceId, IReadOnlyCollection<Guid> teamIds);
}