namespace MulticlassRace.Models;

public class ClassSetting
{
    public Guid ClassSettingId { get; set; }

    public required string ClassSettingName { get; set; }

    public bool IsDefault { get; set; }

    public ICollection<ClassSettingItem> Items { get; set; } = new List<ClassSettingItem>();

    public ICollection<Race> Races { get; set; } = new List<Race>();
}