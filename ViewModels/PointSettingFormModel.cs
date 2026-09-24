namespace MulticlassRace.ViewModels;

public class PointSettingFormModel
{
    public Guid SettingId { get; set; }

    public required string SettingName { get; set; }

    public bool IsDefault { get; set; }

    public List<PointSettingEntryModel> Entries { get; set; } = new();
}