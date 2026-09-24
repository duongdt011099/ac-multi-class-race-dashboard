namespace MulticlassRace.ViewModels;

public class ChampionshipFormModel
{
    public Guid ChampionshipId { get; set; }

    public required string ChampionshipName { get; set; }

    public bool IsActive { get; set; } = true;
}