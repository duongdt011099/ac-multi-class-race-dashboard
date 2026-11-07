namespace MulticlassRace.ViewModels;

public class SeasonModel
{
    public Guid SeasonId { get; set; }

    public required string SeasonName { get; set; }

    public int RaceCount { get; set; }

    /// <summary>Null when the season predates rounds and has not been configured yet.</summary>
    public int? NumberOfRounds { get; set; }

    public bool HasRounds => NumberOfRounds is not null;

    /// <summary>Races of the season that already have a result.</summary>
    public int FinishedRaceCount { get; set; }

    public bool IsFinished { get; set; }

    /// <summary>No more races can be added once the season holds all of its rounds.</summary>
    public bool IsFull => NumberOfRounds is { } rounds && RaceCount >= rounds;

    public Guid? ClassSettingId { get; set; }

    public string? ClassSettingName { get; set; }
}
