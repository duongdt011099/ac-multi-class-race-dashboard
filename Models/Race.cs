namespace MulticlassRace.Models;

public class Race
{
    public Guid RaceId { get; set; }

    public required string RaceName { get; set; }

    public string Country { get; set; } = string.Empty;

    public Guid PointSettingId { get; set; }

    public Guid ClassSettingId { get; set; }

    public required RaceStatus Status { get; set; } = RaceStatus.NotStarted;

    public string? TrackName { get; set; }

    public string? TrackLayout { get; set; }

    public int? NumberOfLaps { get; set; }

    public int? RaceDuration { get; set; }

    /// <summary>Race length in minutes for a timed race; null for an ordinary lap-limited race.</summary>
    public int? TimedRaceMinutes { get; set; }

    /// <summary>Whether a safety car leads the formation lap of this race.</summary>
    public bool UseSafetyCar { get; set; } = true;

    public int? PracticeSessionMinutes { get; set; }

    public int? QualifyingSessionMinutes { get; set; }

    public required Season Season { get; set; }
    
    public required PointSetting PointSetting { get; set; }

    public ClassSetting ClassSetting { get; set; } = null!;

    public required ICollection<Session> Sessions { get; set; } = new List<Session>();

    public required ICollection<Team> EnteredTeams { get; set; } = new List<Team>();
}

public enum RaceStatus
{
    NotStarted,
    InProgress,
    Finished,
}