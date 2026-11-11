using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Teams;

public interface ITeamService
{
    Task<IEnumerable<TeamModel>> GetActiveTeamsAsync();
    Task<TeamModel?> GetTeamByIdAsync(Guid teamId);
    Task DeacivateTeamAsync(Guid teamId);
    Task CreateTeamAsync(TeamFormModel team, IReadOnlyCollection<Guid> driverIds);
    Task BatchUpdateClassesAsync(IReadOnlyCollection<Guid> teamIds, IReadOnlyCollection<Guid> classItemIds);
    Task UpdateTeamAsync(TeamFormModel team, IReadOnlyCollection<Guid> driverIds);
}
