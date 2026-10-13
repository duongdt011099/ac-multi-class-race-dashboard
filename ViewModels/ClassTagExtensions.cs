namespace MulticlassRace.ViewModels;

public static class ClassTagExtensions
{
    /// <summary>"WEC - GT3": the label used for class filters and class pickers.</summary>
    public static string Label(this ClassTagModel tag) => $"{tag.SettingName} - {tag.ItemName}";

    public static IEnumerable<string> DistinctLabels(this IEnumerable<ClassTagModel> tags)
    {
        return tags
            .Select(Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase);
    }

    public static bool HasLabel(this IEnumerable<ClassTagModel> tags, string label)
    {
        return tags.Any(tag => string.Equals(tag.Label(), label, StringComparison.OrdinalIgnoreCase));
    }
}
