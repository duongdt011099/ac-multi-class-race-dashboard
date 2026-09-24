using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Abstractions;

public interface ITeamService
{
    Task<IEnumerable<TeamModel>> GetActiveTeamsAsync();
    Task<TeamModel?> GetTeamByIdAsync(Guid teamId);
    Task DeacivateTeamAsync(Guid teamId);
    Task CreateTeamAsync(TeamFormModel team, IReadOnlyCollection<Guid> driverIds);
    Task UpdateTeamAsync(TeamFormModel team, IReadOnlyCollection<Guid> driverIds);
}
