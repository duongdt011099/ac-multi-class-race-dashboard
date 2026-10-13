using MulticlassRace.Common;
using MulticlassRace.Models;

namespace MulticlassRace.ViewModels;

/// <summary>The conditions a session launches under.</summary>
public sealed record SessionConditions(string Weather, double SunAngle, bool Penalties);

public static class SessionConditionOptions
{
    public const string DefaultWeather = "3_clear";

    public const double DefaultSunAngle = -16;

    /// <summary>Alphabetical by label; the values are what AC and the results need.</summary>
    public static readonly IReadOnlyList<SelectOption> Weather = new[]
    {
        new SelectOption("3_clear", "Clear"),
        new SelectOption("7_heavy_clouds", "Heavy Clouds"),
        new SelectOption("1_heavy_fog", "Heavy Fog"),
        new SelectOption("5_light_clouds", "Light Clouds"),
        new SelectOption("2_light_fog", "Light Fog"),
        new SelectOption("4_mid_clear", "Mid Clear"),
        new SelectOption("6_mid_clouds", "Mid Clouds")
    };

    /// <summary>
    /// Time of day as AC takes it: a sun angle, not a clock. The mapping is
    /// (minutesSinceMidnight / 60) * 16 - 208, which is the whole of the range AC
    /// exposes -80 (8:00) to 80 (18:00). Anything past either end is outside it, so
    /// there is no true night here; a sky mod (Sol or Pure) is what does night in AC.
    /// </summary>
    public static readonly IReadOnlyList<(string Label, double SunAngle)> TimeOfDay = new (string, double)[]
    {
        ("Early Dawn (08:00)", -80),
        ("Morning (10:00)", -48),
        ("Mid-Day (12:00)", -16),
        ("Early Afternoon (14:00)", 16),
        ("Late Afternoon (16:00)", 48),
        ("Sunset (18:00)", 80)
    };

    public static IReadOnlyList<SelectOption> TimeOfDaySelectOptions { get; } =
        TimeOfDay.Select(t => new SelectOption(t.Label, t.Label)).ToList();

    /// <summary>The dropdown's value is the label, so an angle needs its label back.</summary>
    public static string LabelFor(double sunAngle)
    {
        return TimeOfDay.FirstOrDefault(o => o.SunAngle == sunAngle).Label
               ?? TimeOfDay.First(o => o.SunAngle == DefaultSunAngle).Label;
    }
}

/// <summary>
/// Conditions held per session type, so practice, qualifying and the race can each be set up once
/// and left alone.
/// </summary>
public sealed class SessionConditionSet
{
    private readonly Dictionary<SessionType, SessionConditions> _conditions = new();

    public SessionConditionSet()
    {
        Reset();
    }

    public SessionConditions Get(SessionType type)
    {
        return _conditions.TryGetValue(type, out var conditions) ? conditions : Default;
    }

    public void SetWeather(SessionType type, string? weather)
    {
        _conditions[type] = Get(type) with
        {
            Weather = string.IsNullOrWhiteSpace(weather) ? SessionConditionOptions.DefaultWeather : weather
        };
    }

    public void SetTimeOfDay(SessionType type, string? label)
    {
        var match = SessionConditionOptions.TimeOfDay.FirstOrDefault(o => o.Label == label);

        _conditions[type] = Get(type) with
        {
            SunAngle = match.Label is null ? SessionConditionOptions.DefaultSunAngle : match.SunAngle
        };
    }

    public void SetPenalties(SessionType type, bool penalties)
    {
        _conditions[type] = Get(type) with { Penalties = penalties };
    }

    public void Reset()
    {
        foreach (var type in Enum.GetValues<SessionType>())
        {
            _conditions[type] = Default;
        }
    }

    private static SessionConditions Default =>
        new(SessionConditionOptions.DefaultWeather, SessionConditionOptions.DefaultSunAngle, true);
}
