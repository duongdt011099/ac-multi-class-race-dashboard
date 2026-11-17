using MulticlassRace.Common;

namespace MulticlassRace.ViewModels;

/// <summary>
/// The named AI difficulty and aggression levels. Assetto Corsa only knows numbers (AI_LEVEL and
/// AI_AGGRESSION, 0-100), so each name stands for one of them and the number is shown next to it.
/// </summary>
public static class AiLevels
{
    public static readonly IReadOnlyList<(string Name, int Value)> Difficulty = new (string, int)[]
    {
        ("Legend", 100),
        ("Professional", 95),
        ("Hard", 90),
        ("Medium", 80),
        ("Easy", 70)
    };

    public static readonly IReadOnlyList<(string Name, int Value)> Aggression = new (string, int)[]
    {
        ("Hard", 80),
        ("Normal", 50),
        ("Easy", 20)
    };

    public static IEnumerable<SelectOption> DifficultyOptions { get; } = ToOptions(Difficulty);

    public static IEnumerable<SelectOption> AggressionOptions { get; } = ToOptions(Aggression);

    private static List<SelectOption> ToOptions(IReadOnlyList<(string Name, int Value)> levels)
        => levels.Select(l => new SelectOption(l.Value.ToString(), $"{l.Name} ({l.Value})")).ToList();

    /// <summary>The name and number of a level, or just the number for a value that is not one of the named levels.</summary>
    public static string Format(IReadOnlyList<(string Name, int Value)> levels, int? value)
    {
        if (value is null)
        {
            return "—";
        }

        var match = levels.FirstOrDefault(l => l.Value == value);

        return match.Name is null ? value.Value.ToString() : $"{match.Name} ({match.Value})";
    }

    public static int? Parse(string? value) => int.TryParse(value, out var number) ? number : null;
}
