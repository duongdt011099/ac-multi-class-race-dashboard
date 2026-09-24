using MulticlassRace.Models;

namespace MulticlassRace.ViewModels;

public class RaceModel
{
    public Guid RaceId { get; set; }

    public required string RaceName { get; set; }

    public string Country { get; set; } = string.Empty;

    public RaceStatus Status { get; set; }

    public int StandingCount { get; set; }
}