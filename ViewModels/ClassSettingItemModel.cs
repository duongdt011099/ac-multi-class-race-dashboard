namespace MulticlassRace.ViewModels;

public class ClassSettingItemModel
{
    public Guid ClassSettingItemId { get; set; }

    public required string Name { get; set; }

    public int Order { get; set; }

    public required string Color { get; set; }
}