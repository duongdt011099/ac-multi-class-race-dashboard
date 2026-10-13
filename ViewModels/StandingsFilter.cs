namespace MulticlassRace.ViewModels;

public static class StandingsFilter
{
    public static IReadOnlyList<string> ClassNames(IEnumerable<DriverStandingsModel> standings)
    {
        return standings
            .Select(s => s.DriverClass)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<DriverStandingsModel> ByClass(IEnumerable<DriverStandingsModel> standings, string classFilter)
    {
        return string.IsNullOrEmpty(classFilter)
            ? standings.ToList()
            : standings
                .Where(s => string.Equals(s.DriverClass, classFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    public static IReadOnlyList<string> SessionClassNames(IEnumerable<SessionModel> sessions)
    {
        return sessions
            .SelectMany(s => s.DriverStandings)
            .Select(d => d.TeamClassName)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Exact match, as the session tables have always compared class names.</summary>
    public static IEnumerable<DriverStandingModel> ByClass(IEnumerable<DriverStandingModel> standings, string classFilter)
    {
        return string.IsNullOrEmpty(classFilter)
            ? standings
            : standings.Where(s => s.TeamClassName == classFilter);
    }
}
