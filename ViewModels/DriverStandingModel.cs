namespace MulticlassRace.ViewModels;

public class DriverStandingModel
{
    public int Position { get; set; }

    public required string DriverName { get; set; }

    public required string TeamName { get; set; }

    public required string TeamClassName { get; set; }

    public int BestLapTimeMs { get; set; }

    public int Points { get; set; }

    public int LapCount { get; set; }

    public int ClassPosition { get; set; }
}