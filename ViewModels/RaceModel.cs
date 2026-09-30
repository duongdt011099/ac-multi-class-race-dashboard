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

    public string? TrackName { get; set; }

    public string? TrackLayout { get; set; }

    public int? NumberOfLaps { get; set; }

    public int? RaceDuration { get; set; }

    public int? PracticeSessionMinutes { get; set; }

    public int? QualifyingSessionMinutes { get; set; }

    public int StandingCount { get; set; }
}