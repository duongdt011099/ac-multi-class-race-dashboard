using MulticlassRace.Common;

namespace MulticlassRace.ViewModels;

public enum TeamSortColumn
{
    Team,
    Class,
    Status
}

/// <summary>Filtering and sorting behind the teams table.</summary>
public static class TeamListQuery
{
    public static IEnumerable<TeamModel> Apply(
        IEnumerable<TeamModel> teams,
        IReadOnlyCollection<DriverModel> activeDrivers,
        string classFilter,
        string searchTerm,
        SortState<TeamSortColumn> sort)
    {
        var filtered = string.IsNullOrEmpty(classFilter)
            ? teams
            : teams.Where(t => t.ClassTags.HasLabel(classFilter));

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            filtered = filtered.Where(t =>
                t.TeamName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                activeDrivers.Any(d => d.TeamId == t.TeamId &&
                    (d.DriverName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false)));
        }

        return sort.Column switch
        {
            TeamSortColumn.Team => sort.Apply(filtered, t => t.TeamName),
            TeamSortColumn.Class => sort.Apply(filtered, PrimaryClassLabel),
            TeamSortColumn.Status => sort.Apply(filtered, t => t.IsActive),
            _ => filtered
        };
    }

    public static IEnumerable<string> ClassNames(IEnumerable<TeamModel> teams)
        => teams.SelectMany(t => t.ClassTags).DistinctLabels();

    private static string PrimaryClassLabel(TeamModel team)
        => team.ClassTags.Count == 0 ? string.Empty : team.ClassTags[0].Label();
}
