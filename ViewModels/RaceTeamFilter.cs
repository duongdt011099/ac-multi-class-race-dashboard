using MulticlassRace.Common;

namespace MulticlassRace.ViewModels;

/// <summary>
/// Groups teams by the class they hold in one particular class setting, which is the setting the
/// race is using. A team can hold classes from several settings; only the race's one counts here.
/// </summary>
public sealed class RaceTeamFilter
{
    private readonly Guid? _classSettingId;

    public RaceTeamFilter(Guid? classSettingId)
    {
        _classSettingId = classSettingId;
    }

    public ClassTagModel? ClassOf(TeamModel team)
    {
        return _classSettingId is { } id
            ? team.ClassTags.FirstOrDefault(t => t.ClassSettingId == id)
            : null;
    }

    public string ClassName(TeamModel team) => ClassOf(team)?.ItemName ?? string.Empty;

    public string ClassColor(TeamModel team) => ClassOf(team)?.Color ?? RaceFormat.UnknownClassColor;

    public string ClassLabel(TeamModel team) => ClassOf(team)?.Label() ?? string.Empty;

    public IReadOnlyList<string> ClassLabels(IEnumerable<TeamModel> teams)
    {
        return teams
            .Select(ClassLabel)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool HasUncategorized(IEnumerable<TeamModel> teams)
        => teams.Any(t => string.IsNullOrWhiteSpace(ClassName(t)));

    public List<TeamModel> Filter(IEnumerable<TeamModel> teams, string classFilter)
    {
        IEnumerable<TeamModel> filtered = classFilter switch
        {
            "" => teams,
            ClassFilterKeys.Uncategorized => teams.Where(t => string.IsNullOrWhiteSpace(ClassName(t))),
            _ => teams.Where(t => string.Equals(ClassLabel(t), classFilter, StringComparison.OrdinalIgnoreCase))
        };

        return filtered.OrderBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
