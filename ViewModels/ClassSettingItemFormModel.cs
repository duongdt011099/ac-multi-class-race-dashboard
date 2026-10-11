namespace MulticlassRace.ViewModels;

public class ClassSettingItemFormModel
{
    public Guid ClassSettingItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Order { get; set; }

    public string Color { get; set; } = "#52525b";
}