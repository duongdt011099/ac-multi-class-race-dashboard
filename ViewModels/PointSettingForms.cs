namespace MulticlassRace.ViewModels;

/// <summary>Builds and edits <see cref="PointSettingFormModel"/> instances for the point setting dialog.</summary>
public static class PointSettingForms
{
    public static PointSettingFormModel CreateNew(bool isDefault)
    {
        return new PointSettingFormModel
        {
            SettingId = Guid.NewGuid(),
            SettingName = string.Empty,
            IsDefault = isDefault,
            Entries = new List<PointSettingEntryModel>
            {
                new() { Position = "1", Points = "" },
                new() { Position = "2", Points = "" }
            }
        };
    }

    public static PointSettingFormModel From(PointSettingModel setting)
    {
        return new PointSettingFormModel
        {
            SettingId = setting.SettingId,
            SettingName = setting.SettingName,
            IsDefault = setting.IsDefault,
            Entries = setting.Config
                .OrderBy(kv => int.Parse(kv.Key))
                .Select(kv => new PointSettingEntryModel
                {
                    Position = kv.Key,
                    Points = kv.Value.ToString()
                })
                .ToList()
        };
    }

    /// <summary>Appends a row numbered one past the highest position already entered.</summary>
    public static void AddEntry(this PointSettingFormModel form)
    {
        var nextPosition = form.Entries
            .Select(e => int.TryParse(e.Position, out var position) ? position : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        form.Entries.Add(new PointSettingEntryModel { Position = nextPosition.ToString() });
    }

    /// <summary>Compact "1:25 › 2:18" summary of a points table, ordered by position.</summary>
    public static string FormatPreview(IReadOnlyDictionary<string, int> config)
    {
        return string.Join(" › ", config
            .OrderBy(kv => int.Parse(kv.Key))
            .Select(kv => $"{kv.Key}:{kv.Value}"));
    }
}
