namespace MulticlassRace.ViewModels;

/// <summary>One row of a season's final table.</summary>
public sealed record SeasonStandingRow(int Position, string DriverName, string TeamName, string ClassName, int Points, bool IsHuman);

/// <summary>What happened when the last round of a season was finished: the final table of the human driver's class.</summary>
public sealed record SeasonCompletion(
    Guid SeasonId,
    string SeasonName,
    string ChampionshipName,
    string? DriverName,
    string? ClassName,
    int? HumanPosition,
    int HumanPoints,
    IReadOnlyList<SeasonStandingRow> Table)
{
    public bool IsWorldChampion => HumanPosition == 1;

    public IReadOnlyList<SeasonStandingRow> Podium => Table.Take(3).ToList();
}
