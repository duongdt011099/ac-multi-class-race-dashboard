namespace MulticlassRace.Models;

public class Race
{
    public Guid RaceId { get; set; }

    public required string RaceName { get; set; }

    public string Country { get; set; } = string.Empty;

    public Guid PointSettingId { get; set; }

    public required RaceStatus Status { get; set; } = RaceStatus.NotStarted;

    public required Season Season { get; set; }
    
    public required PointSetting PointSetting { get; set; }

    public required ICollection<Session> Sessions { get; set; } = new List<Session>();

    public required ICollection<Team> EnteredTeams { get; set; } = new List<Team>();
}

public enum RaceStatus
{
    NotStarted,
    InProgress,
    Finished,
}