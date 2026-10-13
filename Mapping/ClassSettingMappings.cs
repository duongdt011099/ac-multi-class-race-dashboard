using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Mapping;

public static class ClassSettingMappings
{
    public static ClassTagModel ToClassTag(this ClassSettingItem item)
    {
        return new ClassTagModel
        {
            ClassSettingItemId = item.ClassSettingItemId,
            ClassSettingId = item.ClassSettingId,
            ItemName = item.Name,
            SettingName = item.ClassSetting.ClassSettingName,
            Color = item.Color,
            Order = item.Order
        };
    }

    /// <summary>Tags grouped by class setting name, then by rank within the setting.</summary>
    public static List<ClassTagModel> ToClassTags(this IEnumerable<ClassSettingItem> items)
    {
        return items
            .OrderBy(i => i.ClassSetting.ClassSettingName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Order)
            .Select(ToClassTag)
            .ToList();
    }

    public static ClassSettingModel ToModel(this ClassSetting setting)
    {
        return new ClassSettingModel
        {
            ClassSettingId = setting.ClassSettingId,
            ClassSettingName = setting.ClassSettingName,
            IsDefault = setting.IsDefault,
            Items = setting.Items
                .OrderBy(i => i.Order)
                .Select(i => new ClassSettingItemModel
                {
                    ClassSettingItemId = i.ClassSettingItemId,
                    Name = i.Name,
                    Order = i.Order,
                    Color = i.Color
                })
                .ToList()
        };
    }

    public static PointSettingModel ToModel(this PointSetting setting)
    {
        return new PointSettingModel
        {
            SettingId = setting.SettingId,
            SettingName = setting.SettingName,
            IsDefault = setting.IsDefault,
            Config = setting.Config
        };
    }
}
