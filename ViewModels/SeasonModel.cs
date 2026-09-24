namespace MulticlassRace.ViewModels;

public class SeasonModel
{
    public Guid SeasonId { get; set; }

    public required string SeasonName { get; set; }

    public int RaceCount { get; set; }
}