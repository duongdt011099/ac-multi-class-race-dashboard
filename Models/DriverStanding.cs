namespace MulticlassRace.Models;

public class DriverStanding
{
    public Guid DriverStandingId { get; set; }

    public required Driver Driver { get; set; }
    
    public required Guid OriginalTeamId { get; set; }

    public required string TeamName { get; set; }

    public required Race Race { get; set; }

    public int Position { get; set; }

    public int Points { get; set; }

    public int BestLapTimeMs { get; set; }
}