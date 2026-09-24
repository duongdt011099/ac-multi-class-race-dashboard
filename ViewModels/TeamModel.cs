namespace MulticlassRace.ViewModels;

public class TeamModel
{
    public Guid TeamId { get; set; }

    public required string TeamName { get; set; }

    public string? TeamLogo { get; set; }

    public required string TeamClass { get; set; }

    public Guid TeamClassId { get; set; }

    public bool IsActive { get; set; } = true;

    public List<DriverModel> Drivers { get; set; } = new();
}
