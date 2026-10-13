using MulticlassRace.Models;

namespace MulticlassRace.Services;

/// <summary>
/// Orders drivers and teams by the class they hold in the race's class setting. The first class of
/// the setting leads the grid and the results; anything without a class goes last.
/// </summary>
internal static class RaceClassOrdering
{
    public static Dictionary<Guid, int> OrderById(Race? race)
    {
        if (race?.ClassSetting?.Items is not { Count: > 0 } items)
        {
            return new Dictionary<Guid, int>();
        }

        return items
            .OrderBy(i => i.Order)
            .Select((i, index) => (i.ClassSettingItemId, index))
            .ToDictionary(x => x.ClassSettingItemId, x => x.index);
    }

    public static Dictionary<string, int> OrderByName(Race? race)
    {
        if (race?.ClassSetting?.Items is not { Count: > 0 } items)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        return items
            .OrderBy(i => i.Order)
            .Select((i, index) => (ItemName: i.Name.Trim(), index))
            .ToDictionary(x => x.ItemName, x => x.index, StringComparer.OrdinalIgnoreCase);
    }

    public static int IndexOf(ClassSettingItem? item, IReadOnlyDictionary<Guid, int> orderByItemId)
    {
        return item is not null && orderByItemId.TryGetValue(item.ClassSettingItemId, out var index) ? index : int.MaxValue;
    }

    public static int IndexOfName(string? className, IReadOnlyDictionary<string, int> orderByName)
    {
        return !string.IsNullOrWhiteSpace(className) && orderByName.TryGetValue(className.Trim(), out var index) ? index : int.MaxValue;
    }

    /// <summary>The team's class within the race's own class setting; teams can hold classes from several settings.</summary>
    public static ClassSettingItem? ClassItemOf(Race race, Team? team)
    {
        return team?.ClassSettingItems.FirstOrDefault(i => i.ClassSettingId == race.ClassSettingId);
    }

    public static string ClassNameOf(Race race, Team team)
    {
        return ClassItemOf(race, team)?.Name ?? string.Empty;
    }
}
