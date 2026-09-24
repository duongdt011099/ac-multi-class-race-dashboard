using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface ITeamRepository : IGenericRepository<Team>
{
    Task<IEnumerable<Team>> GetActiveTeamsAsync();
    Task<Team?> GetTeamDetailsAsync(Guid teamId);
    Task DeacivateTeamAsync(Guid teamId);
    Task CreateTeamAsync(Team team, IReadOnlyCollection<Guid> driverIds);
    Task UpdateTeamAsync(Team team, IReadOnlyCollection<Guid> driverIds);
}
