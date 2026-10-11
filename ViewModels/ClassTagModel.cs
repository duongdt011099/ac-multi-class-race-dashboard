namespace MulticlassRace.ViewModels;

public class ClassTagModel
{
    public Guid ClassSettingItemId { get; set; }

    public Guid ClassSettingId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string SettingName { get; set; } = string.Empty;

    public string Color { get; set; } = "#52525b";

    public int Order { get; set; }
}
