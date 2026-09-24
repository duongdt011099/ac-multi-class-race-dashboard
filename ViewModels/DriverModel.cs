namespace MulticlassRace.ViewModels;

public class DriverModel
{
    public Guid DriverId { get; set; }

    public required string DriverName { get; set; }

    public required string CarDisplayName { get; set; }

    public required string Car { get; set; }

    public required string Skin { get; set; }

    public string? LiveryImageUrl { get; set; }

    public string? CarImageUrl { get; set; }

    public string? Nationality { get; set; }

    public int? Age { get; set; }

    public int? DriverAgression { get; set; }

    public int? DriverStrength { get; set; }

    public string? TeamName { get; set; }

    public Guid? TeamId { get; set; }

    public string? TeamLogo { get; set; }

    public string? TeamClassName { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsHuman { get; set; }
}
