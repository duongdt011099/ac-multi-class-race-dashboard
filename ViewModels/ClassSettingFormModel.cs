namespace MulticlassRace.ViewModels;

public class ClassSettingFormModel
{
    public Guid ClassSettingId { get; set; }

    public required string ClassSettingName { get; set; }

    public bool IsDefault { get; set; }

    public List<ClassSettingItemFormModel> Items { get; set; } = new();
}