namespace MulticlassRace.Models;

/// <summary>
/// The one saved Quick Race setup: race settings, grid mode and the custom / class-system grids.
/// Stored as a JSON document because a quick race has no teams, drivers or classes of its own in the database.
/// </summary>
public class QuickRaceSetup
{
    public Guid QuickRaceSetupId { get; set; }

    /// <summary>The serialized <c>QuickRaceSetupModel</c>.</summary>
    public string SetupJson { get; set; } = "{}";

    /// <summary>
    /// The grid exactly as it was handed to the game, so a result that arrives later is read against the
    /// cars that really raced even if the grid was edited in the meantime.
    /// </summary>
    public string? LastLaunchJson { get; set; }

    public DateTime UpdatedUtc { get; set; }
}

/// <summary>One finished quick race: kept for the history list and its result table.</summary>
public class QuickRaceResult
{
    public Guid QuickRaceResultId { get; set; }

    public DateTime RaceDate { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? TrackName { get; set; }

    public string? TrackLayout { get; set; }

    public int DriverCount { get; set; }

    /// <summary>Overall finishing position of the human driver; null when the human did not take part.</summary>
    public int? HumanPosition { get; set; }

    public int? HumanClassPosition { get; set; }

    public string? HumanClassName { get; set; }

    /// <summary>The serialized list of <c>QuickRaceResultRow</c>.</summary>
    public string ResultJson { get; set; } = "[]";
}

/// <summary>A saved Quick Race grid (its classes and drivers) the user can load back onto the Drivers tab.</summary>
public class QuickRacePreset
{
    public Guid QuickRacePresetId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The serialized <c>QuickRacePresetGrid</c>.</summary>
    public string GridJson { get; set; } = "{}";

    public DateTime UpdatedUtc { get; set; }
}
