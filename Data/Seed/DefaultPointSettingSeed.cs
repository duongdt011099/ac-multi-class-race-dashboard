using MulticlassRace.Models;

namespace MulticlassRace.Data.Seed;

/// <summary>The "Standard" points table (25-18-15-12-10-8-6-4-2-1) a fresh database starts with.</summary>
public static class DefaultPointSettingSeed
{
    public static PointSetting Setting() => new()
    {
        SettingId = new Guid(SeedIds.DefaultPointSettingId),
        SettingName = "Standard",
        IsDefault = true,
        Config = new Dictionary<string, int>
        {
            ["1"] = 25,
            ["2"] = 18,
            ["3"] = 15,
            ["4"] = 12,
            ["5"] = 10,
            ["6"] = 8,
            ["7"] = 6,
            ["8"] = 4,
            ["9"] = 2,
            ["10"] = 1
        }
    };
}
