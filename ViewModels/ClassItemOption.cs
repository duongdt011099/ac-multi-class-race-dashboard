namespace MulticlassRace.ViewModels;

/// <summary>One selectable class (a class setting item plus the setting it belongs to) in a class picker.</summary>
public sealed class ClassItemOption
{
    public Guid Id { get; init; }

    public Guid SettingId { get; init; }

    public required string SettingName { get; init; }

    public required string Name { get; init; }

    public required string Color { get; init; }

    public int Order { get; init; }

    public string Label => $"{SettingName} - {Name}";

    public static List<ClassItemOption> FromSettings(IEnumerable<ClassSettingModel> settings)
    {
        return settings
            .SelectMany(s => s.Items.Select(i => new ClassItemOption
            {
                Id = i.ClassSettingItemId,
                SettingId = s.ClassSettingId,
                SettingName = s.ClassSettingName,
                Name = i.Name,
                Color = i.Color,
                Order = i.Order
            }))
            .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Options still available to add. Once a class is chosen only classes of the same rank (Order)
    /// are offered, so a team cannot mix a top class from one setting with a lower class from another.
    /// </summary>
    public static List<ClassItemOption> Available(
        IReadOnlyCollection<ClassItemOption> all,
        IReadOnlyCollection<Guid> selectedIds,
        string? searchText)
    {
        var selected = selectedIds.ToHashSet();
        var selectedOrders = all.Where(o => selected.Contains(o.Id)).Select(o => o.Order).ToHashSet();

        IEnumerable<ClassItemOption> results = all.Where(o => !selected.Contains(o.Id));

        if (selectedOrders.Count > 0)
        {
            results = results.Where(o => selectedOrders.Contains(o.Order));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            results = results.Where(o => o.Label.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        return results
            .OrderBy(o => o.SettingName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Order)
            .ToList();
    }

    /// <summary>The options matching the selected ids, in selection order; unknown ids are skipped.</summary>
    public static List<ClassItemOption> Selected(IReadOnlyCollection<ClassItemOption> all, IEnumerable<Guid> selectedIds)
    {
        var byId = all.ToDictionary(o => o.Id);

        return selectedIds
            .Select(id => byId.TryGetValue(id, out var option) ? option : null)
            .OfType<ClassItemOption>()
            .ToList();
    }
}
