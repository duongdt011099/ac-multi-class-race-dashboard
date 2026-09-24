namespace MulticlassRace.Models;

public class DriverStanding
{
    public Guid DriverStandingId { get; set; }

    public required Driver Driver { get; set; }

    public required string DriverName { get; set; }

    public required Guid OriginalTeamId { get; set; }

    public required string TeamName { get; set; }

    public required string TeamClassName { get; set; }

    public required Race Race { get; set; }

    public int Position { get; set; }

    public int ClassPosition { get; set; }

    public int Points { get; set; }

    public int BestLapTimeMs { get; set; }

    public int LapCount { get; set; }
}