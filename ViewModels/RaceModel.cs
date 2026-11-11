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

    public Guid ClassSettingId { get; set; }

    public string ClassSettingName { get; set; } = string.Empty;

    public string? TrackName { get; set; }

    public string? TrackLayout { get; set; }

    public int? NumberOfLaps { get; set; }

    public int? RaceDuration { get; set; }

    public int? TimedRaceMinutes { get; set; }

    /// <summary>Whether a safety car leads the formation lap of this race.</summary>
    public bool UseSafetyCar { get; set; } = true;

    public int? PracticeSessionMinutes { get; set; }

    public int? QualifyingSessionMinutes { get; set; }

    public int StandingCount { get; set; }
}