using MulticlassRace.Models;

namespace MulticlassRace.ViewModels;

public class RaceModel
{
    public Guid RaceId { get; set; }

    public required string RaceName { get; set; }

    public string Country { get; set; } = string.Empty;

    public RaceStatus Status { get; set; }

    public Guid PointSettingId { get; set; }

    public string PointSettingName { get; set; } = string.Empty;

    public int StandingCount { get; set; }
}