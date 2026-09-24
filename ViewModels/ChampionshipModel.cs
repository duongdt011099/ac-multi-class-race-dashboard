namespace MulticlassRace.ViewModels;

public class ChampionshipModel
{
    public Guid ChampionshipId { get; set; }

    public required string ChampionshipName { get; set; }

    public int SeasonCount { get; set; }

    public bool IsActive { get; set; } = true;
}