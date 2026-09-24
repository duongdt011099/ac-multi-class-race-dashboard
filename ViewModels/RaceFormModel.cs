namespace MulticlassRace.ViewModels;

public class RaceFormModel
{
    public Guid RaceId { get; set; }

    public required string RaceName { get; set; }

    public string Country { get; set; } = string.Empty;

    public Guid SeasonId { get; set; }

    public Guid PointSettingId { get; set; }
}