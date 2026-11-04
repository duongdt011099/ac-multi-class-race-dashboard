namespace MulticlassRace.ViewModels;

public class SeasonModel
{
    public Guid SeasonId { get; set; }

    public required string SeasonName { get; set; }

    public int RaceCount { get; set; }

    public Guid? ClassSettingId { get; set; }

    public string? ClassSettingName { get; set; }
}