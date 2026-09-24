namespace MulticlassRace.ViewModels;

public class PointSettingModel
{
    public Guid SettingId { get; set; }

    public required string SettingName { get; set; }

    public bool IsDefault { get; set; }

    public IReadOnlyDictionary<string, int> Config { get; set; } = new Dictionary<string, int>();
}