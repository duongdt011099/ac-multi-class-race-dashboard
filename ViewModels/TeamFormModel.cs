namespace MulticlassRace.ViewModels;

public class TeamFormModel
{
    public Guid TeamId { get; set; }

    public required string TeamName { get; set; }

    public string? TeamLogo { get; set; }

    public Guid TeamClassId { get; set; }

    public bool IsActive { get; set; } = true;
}
