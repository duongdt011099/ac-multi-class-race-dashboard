namespace MulticlassRace.Models;

public class TeamClass
{
    public Guid TeamClassId { get; set; }

    public required string TeamClassName { get; set; }

    public ICollection<Team> Teams { get; set; } = new List<Team>();

    public bool IsActive { get; set; } = true;
}