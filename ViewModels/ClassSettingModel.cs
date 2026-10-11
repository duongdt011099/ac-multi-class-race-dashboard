namespace MulticlassRace.ViewModels;

public class ClassSettingModel
{
    public Guid ClassSettingId { get; set; }

    public required string ClassSettingName { get; set; }

    public bool IsDefault { get; set; }

    public List<ClassSettingItemModel> Items { get; set; } = new();
}