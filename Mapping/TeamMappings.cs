using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Mapping;

public static class TeamMappings
{
    /// <summary>
    /// The team with its class tags. Drivers are only mapped on request, because most screens list
    /// teams without them and the drivers are not always loaded.
    /// </summary>
    public static TeamModel ToModel(this Team team, bool includeDrivers = false)
    {
        return new TeamModel
        {
            TeamId = team.TeamId,
            TeamName = team.TeamName,
            TeamLogo = team.TeamLogo,
            ClassTags = team.ClassSettingItems.ToClassTags(),
            IsActive = team.IsActive,
            Drivers = includeDrivers
                ? team.Drivers.Select(d => d.ToSummaryModel()).ToList()
                : new List<DriverModel>()
        };
    }
}
