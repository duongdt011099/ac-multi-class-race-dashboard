namespace MulticlassRace.ViewModels;

/// <summary>Looks up the colour of a class by name across every class setting.</summary>
public sealed class ClassColorMap
{
    private readonly Dictionary<string, string> _colors;

    private ClassColorMap(Dictionary<string, string> colors)
    {
        _colors = colors;
    }

    public static ClassColorMap Empty { get; } = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>When two settings share a class name, the first one (by setting order) wins.</summary>
    public static ClassColorMap From(IEnumerable<ClassSettingModel> settings)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var setting in settings)
        {
            foreach (var item in setting.Items.OrderBy(i => i.Order))
            {
                map.TryAdd(item.Name.Trim(), item.Color);
            }
        }

        return new ClassColorMap(map);
    }

    public string ColorFor(string? className)
    {
        return !string.IsNullOrWhiteSpace(className) && _colors.TryGetValue(className.Trim(), out var color)
            ? color
            : RaceFormat.UnknownClassColor;
    }
}
