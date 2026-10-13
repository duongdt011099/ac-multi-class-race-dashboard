using MulticlassRace.Mapping;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Teams;

public class TeamService : ITeamService
{
    private readonly ITeamRepository _teamRepository;

    public TeamService(ITeamRepository teamRepository)
    {
        _teamRepository = teamRepository;
    }

    public Task DeacivateTeamAsync(Guid teamId)
    {
        return _teamRepository.DeacivateTeamAsync(teamId);
    }

    public async Task<IEnumerable<TeamModel>> GetActiveTeamsAsync()
    {
        var teams = await _teamRepository.GetActiveTeamsAsync();

        return teams
            .Select(t => t.ToModel())
            .OrderBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<TeamModel?> GetTeamByIdAsync(Guid teamId)
    {
        var team = await _teamRepository.GetTeamDetailsAsync(teamId);

        return team?.ToModel(includeDrivers: true);
    }

    public Task CreateTeamAsync(TeamFormModel team, IReadOnlyCollection<Guid> driverIds)
    {
        return _teamRepository.CreateTeamAsync(ToTeam(team), team.ClassSettingItemIds, driverIds);
    }

    public Task UpdateTeamAsync(TeamFormModel team, IReadOnlyCollection<Guid> driverIds)
    {
        return _teamRepository.UpdateTeamAsync(ToTeam(team), team.ClassSettingItemIds, driverIds);
    }

    private static Models.Team ToTeam(TeamFormModel model)
    {
        return new Models.Team
        {
            TeamId = model.TeamId,
            TeamName = model.TeamName,
            TeamLogo = model.TeamLogo,
            IsActive = model.IsActive
        };
    }
}