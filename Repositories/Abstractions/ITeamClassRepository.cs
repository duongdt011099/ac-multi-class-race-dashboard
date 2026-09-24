using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface ITeamClassRepository : IGenericRepository<TeamClass>
{
    public Task DeactivateTeamClassAsync(Guid teamClassId);

    public Task<IEnumerable<TeamClass>> GetActiveTeamClassesAsync();
}