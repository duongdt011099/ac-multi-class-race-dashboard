namespace MulticlassRace.Models;

public class ClassSettingItem
{
    public Guid ClassSettingItemId { get; set; }

    public Guid ClassSettingId { get; set; }

    public ClassSetting ClassSetting { get; set; } = null!;

    public required string Name { get; set; }

    public int Order { get; set; }

    public required string Color { get; set; }

    public ICollection<Team> Teams { get; set; } = new List<Team>();
}