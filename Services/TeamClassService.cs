using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class TeamClassService : ITeamClassService
{
    private readonly ITeamClassRepository _teamClassRepository;

    public TeamClassService(ITeamClassRepository teamClassRepository)
    {
        _teamClassRepository = teamClassRepository;
    }

    public async Task<IEnumerable<TeamClassModel>> GetActiveTeamClassesAsync()
    {
        var classes = await _teamClassRepository.GetActiveTeamClassesAsync();

        return classes.Select(c => new TeamClassModel
        {
            TeamClassId = c.TeamClassId,
            TeamClassName = c.TeamClassName
        });
    }
}
