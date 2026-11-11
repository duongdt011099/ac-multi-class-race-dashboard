namespace MulticlassRace.ViewModels;

public class RaceFormModel
{
    public Guid RaceId { get; set; }

    public required string RaceName { get; set; }

    public string Country { get; set; } = string.Empty;

    public Guid SeasonId { get; set; }

    public Guid PointSettingId { get; set; }

    public Guid ClassSettingId { get; set; }

    public string? TrackName { get; set; }

    public string? TrackLayout { get; set; }

    public int? NumberOfLaps { get; set; }

    public int? RaceDuration { get; set; }

    /// <summary>Race length in minutes for a timed race; null for a lap-limited race.</summary>
    public int? TimedRaceMinutes { get; set; }

    public bool UseSafetyCar { get; set; } = true;

    public int? PracticeSessionMinutes { get; set; }

    public int? QualifyingSessionMinutes { get; set; }
}