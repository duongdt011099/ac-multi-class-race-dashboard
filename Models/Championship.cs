namespace MulticlassRace.Models;

public class Championship
{
    public Guid ChampionshipId { get; set; }

    public required string ChampionshipName { get; set; }

    public ICollection<Season> Seasons { get; set; } = new List<Season>();

    public bool IsActive { get; set; } = true;
}