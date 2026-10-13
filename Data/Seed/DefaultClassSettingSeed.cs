using MulticlassRace.Models;

namespace MulticlassRace.Data.Seed;

/// <summary>The WEC class setting (Hypercar, LMP2, GT3) a fresh database starts with.</summary>
public static class DefaultClassSettingSeed
{
    public static ClassSetting Setting() => new()
    {
        ClassSettingId = new Guid(SeedIds.DefaultClassSettingId),
        ClassSettingName = "WEC",
        IsDefault = true
    };

    public static ClassSettingItem[] Items() =>
    [
        new ClassSettingItem
        {
            ClassSettingItemId = new Guid(SeedIds.DefaultHypercarItemId),
            ClassSettingId = new Guid(SeedIds.DefaultClassSettingId),
            Name = "Hypercar",
            Order = 1,
            Color = "#e10600"
        },
        new ClassSettingItem
        {
            ClassSettingItemId = new Guid(SeedIds.DefaultLmp2ItemId),
            ClassSettingId = new Guid(SeedIds.DefaultClassSettingId),
            Name = "LMP2",
            Order = 2,
            Color = "#0057b8"
        },
        new ClassSettingItem
        {
            ClassSettingItemId = new Guid(SeedIds.DefaultGt3ItemId),
            ClassSettingId = new Guid(SeedIds.DefaultClassSettingId),
            Name = "GT3",
            Order = 3,
            Color = "#3fa34d"
        }
    ];
}
