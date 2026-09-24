namespace MulticlassRace.Models;

public class PointSetting
{
    public Guid SettingId { get; set; }

    public required bool IsDefault { get; set; }

    public required string SettingName { get; set; }

    public required Dictionary<string, int> Config { get; set; } = new Dictionary<string, int>();
}

