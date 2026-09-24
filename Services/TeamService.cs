using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

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

        return teams.Select(t => MapTeam(t, includeDrivers: false));
    }

    public async Task<TeamModel?> GetTeamByIdAsync(Guid teamId)
    {
        var team = await _teamRepository.GetTeamDetailsAsync(teamId);

        return team is null ? null : MapTeam(team, includeDrivers: true);
    }

    public Task CreateTeamAsync(TeamFormModel team, IReadOnlyCollection<Guid> driverIds)
    {
        return _teamRepository.CreateTeamAsync(ToTeam(team), driverIds);
    }

    public Task UpdateTeamAsync(TeamFormModel team, IReadOnlyCollection<Guid> driverIds)
    {
        return _teamRepository.UpdateTeamAsync(ToTeam(team), driverIds);
    }

    private static TeamModel MapTeam(Models.Team team, bool includeDrivers)
    {
        return new TeamModel
        {
            TeamId = team.TeamId,
            TeamName = team.TeamName,
            TeamLogo = team.TeamLogo,
            TeamClass = team.TeamClass.TeamClassName,
            TeamClassId = team.TeamClass.TeamClassId,
            IsActive = team.IsActive,
            Drivers = includeDrivers
                ? team.Drivers.Select(MapDriver).ToList()
                : new List<DriverModel>()
        };
    }

    private static DriverModel MapDriver(Models.Driver driver)
    {
        return new DriverModel
        {
            DriverId = driver.DriverId,
            DriverName = driver.DriverName,
            CarDisplayName = driver.CarDisplayName,
            Car = driver.Car,
            Skin = driver.Skin,
            Nationality = driver.Nationality,
            TeamId = driver.Team?.TeamId,
            TeamLogo = driver.Team?.TeamLogo,
            IsActive = driver.IsActive
        };
    }

    private static Models.Team ToTeam(TeamFormModel model)
    {
        return new Models.Team
        {
            TeamId = model.TeamId,
            TeamName = model.TeamName,
            TeamLogo = model.TeamLogo,
            TeamClass = new Models.TeamClass
            {
                TeamClassId = model.TeamClassId,
                TeamClassName = string.Empty
            },
            IsActive = model.IsActive
        };
    }
}
