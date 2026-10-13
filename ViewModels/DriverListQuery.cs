using MulticlassRace.Common;

namespace MulticlassRace.ViewModels;

public enum DriverSortColumn
{
    Driver,
    Status
}

/// <summary>Filtering and sorting behind the drivers table.</summary>
public static class DriverListQuery
{
    /// <summary>Filter value that selects drivers with no class at all.</summary>
    public const string UncategorizedKey = ClassFilterKeys.Uncategorized;

    public static IEnumerable<DriverModel> Apply(
        IEnumerable<DriverModel> drivers,
        string classFilter,
        string searchTerm,
        SortState<DriverSortColumn> sort)
    {
        var filtered = classFilter switch
        {
            "" => drivers,
            UncategorizedKey => drivers.Where(d => d.ClassTags.Count == 0),
            _ => drivers.Where(d => d.ClassTags.HasLabel(classFilter))
        };

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            filtered = filtered.Where(d =>
                d.DriverName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (d.TeamName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return sort.Column switch
        {
            DriverSortColumn.Driver => sort.Apply(filtered, d => d.DriverName),
            DriverSortColumn.Status => sort.Apply(filtered, d => d.IsActive),
            _ => filtered
        };
    }

    public static IEnumerable<string> ClassNames(IEnumerable<DriverModel> drivers)
        => drivers.SelectMany(d => d.ClassTags).DistinctLabels();

    public static bool HasUncategorized(IEnumerable<DriverModel> drivers)
        => drivers.Any(d => d.ClassTags.Count == 0);
}
