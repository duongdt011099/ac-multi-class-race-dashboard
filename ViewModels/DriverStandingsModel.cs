namespace MulticlassRace.ViewModels;

public class DriverStandingsModel
{
    public Guid SeasonId { get; set; }
    
    public required string DriverName { get; set; }

    public required string DriverClass { get; set; }

    public string? TeamLogoUrl { get; set; }

    public required string TeamName { get; set; }

    public int Points { get; set; }
}