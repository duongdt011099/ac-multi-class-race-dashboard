namespace MulticlassRace.Models;

public class Team
{
    public Guid TeamId { get; set; }

    public required string TeamName { get; set; }

    public string? TeamLogo { get; set; }

    public required TeamClass TeamClass { get; set; }

    public ICollection<Driver> Drivers { get; set; } = new List<Driver>();

    public bool IsActive { get; set; } = true;
}