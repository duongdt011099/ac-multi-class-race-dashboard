namespace MulticlassRace.ViewModels;

public class DriverStandingModel
{
    public int Position { get; set; }

    public required string DriverName { get; set; }

    public required string TeamName { get; set; }

    public int BestLapTimeMs { get; set; }

    public int Points { get; set; }
}