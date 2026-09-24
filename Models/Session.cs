namespace MulticlassRace.Models;

public class Session
{
    public Guid SessionId { get; set; }
    public SessionType SessionType { get; set; }
    public DateTime SessionDate { get; set; }
    public required Guid RaceId { get; set; }
    public required Race Race { get; set; }
    public required ICollection<DriverStanding> DriverStandings { get; set; } = new List<DriverStanding>();
}

public enum SessionType
{
    Practice,
    Qualifying,
    Race
}