using MulticlassRace.Models;
using System.Text.Json.Serialization;
using MulticlassRace.Common;

namespace MulticlassRace.ViewModels;

public enum QuickRaceGridMode
{
    /// <summary>The user builds the classes (columns) and adds every driver by hand or from the teams.</summary>
    Custom,

    /// <summary>The columns are the classes of an existing class setting.</summary>
    ExistingClasses
}

/// <summary>The race settings of a quick race: the Edit Race form without a name, point setting and class setting.</summary>
public class QuickRaceSettingsModel
{
    public string Country { get; set; } = string.Empty;

    public string? TrackName { get; set; }

    public string? TrackLayout { get; set; }

    public int? NumberOfLaps { get; set; } = 10;

    /// <summary>Race length in minutes for a timed race; null for a lap-limited race.</summary>
    public int? TimedRaceMinutes { get; set; }

    public bool UseSafetyCar { get; set; } = true;

    /// <summary>Car of the human driver; null follows the human driver shown in the side menu.</summary>
    public string? HumanCar { get; set; }

    public string? HumanSkin { get; set; }

    public string Weather { get; set; } = SessionConditionOptions.DefaultWeather;

    public double SunAngle { get; set; } = SessionConditionOptions.DefaultSunAngle;

    public bool Penalties { get; set; } = true;

    /// <summary>Run a qualifying session first; its result decides the start order of the cars.</summary>
    public bool QualifyingEnabled { get; set; }

    public int QualifyingMinutes { get; set; } = 20;

    /// <summary>AI_LEVEL (70-100) for every opponent that has no level of its own.</summary>
    public int AiDifficulty { get; set; } = 100;

    /// <summary>AI_AGGRESSION for every opponent that has no aggression of its own.</summary>
    public int AiAggression { get; set; } = 50;
}

/// <summary>One car on the grid. The human entry carries no car of its own: it follows the race settings.</summary>
public class QuickRaceEntryModel
{
    public Guid EntryId { get; set; } = Guid.NewGuid();

    public string DriverName { get; set; } = string.Empty;

    public string Car { get; set; } = string.Empty;

    public string Skin { get; set; } = string.Empty;

    public string CarDisplayName { get; set; } = string.Empty;

    public string? TeamName { get; set; }

    public string? Nationality { get; set; }

    public int? AiStrength { get; set; }

    public int? AiAggression { get; set; }

    public bool IsHuman { get; set; }

    /// <summary>The team driver this entry was copied from; null for a custom driver.</summary>
    public Guid? SourceDriverId { get; set; }

    /// <summary>Best qualifying lap in ms that decided this car's place on the grid; null before qualifying.</summary>
    public int? QualifyingTimeMs { get; set; }
}

/// <summary>A column of the grid. The top entry starts first.</summary>
public class QuickRaceClassModel
{
    public Guid ClassId { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#e10600";

    public List<QuickRaceEntryModel> Entries { get; set; } = new();
}

/// <summary>Everything a quick race remembers between visits.</summary>
public class QuickRaceSetupModel
{
    public QuickRaceSettingsModel Settings { get; set; } = new();

    public QuickRaceGridMode Mode { get; set; } = QuickRaceGridMode.Custom;

    /// <summary>The class setting the columns come from when <see cref="Mode"/> is ExistingClasses.</summary>
    public Guid? ClassSettingId { get; set; }

    /// <summary>Both grids are kept so switching mode never throws a built grid away.</summary>
    public List<QuickRaceClassModel> CustomClasses { get; set; } = new();

    public List<QuickRaceClassModel> SystemClasses { get; set; } = new();

    /// <summary>Qualifying has been run for the cars now on the grid, so their order is the qualifying result.</summary>
    public bool QualifyingCompleted { get; set; }

    /// <summary>The qualifying order the grid follows; copied into the race result so it is kept with the race.</summary>
    public List<QuickRaceResultRow> QualifyingRows { get; set; } = new();

    [JsonIgnore]
    public List<QuickRaceClassModel> ActiveClasses => Mode == QuickRaceGridMode.Custom ? CustomClasses : SystemClasses;
}

/// <summary>The human driver as the quick race will use them: settings override the side menu driver.</summary>
public sealed record QuickRaceHuman(string DriverName, string Car, string Skin, string CarDisplayName)
{
    public bool HasCar => !string.IsNullOrWhiteSpace(Car);
}

/// <summary>One car of the launched grid, indexed as in race.ini (CAR_n), so a result can be read against it.</summary>
public class QuickRaceLaunchCar
{
    public int Index { get; set; }

    public string DriverName { get; set; } = string.Empty;

    public string Car { get; set; } = string.Empty;

    public string Skin { get; set; } = string.Empty;

    public string CarDisplayName { get; set; } = string.Empty;

    public string? TeamName { get; set; }

    public string ClassName { get; set; } = string.Empty;

    public string ClassColor { get; set; } = RaceFormat.UnknownClassColor;

    public bool IsHuman { get; set; }
}

public class QuickRaceLaunchSnapshot
{
    public DateTime LaunchedUtc { get; set; }

    public string? TrackName { get; set; }

    public SessionType SessionType { get; set; } = SessionType.Race;

    public string? TrackLayout { get; set; }

    public List<QuickRaceLaunchCar> Cars { get; set; } = new();
}

public class QuickRaceResultRow
{
    public int Position { get; set; }

    public int ClassPosition { get; set; }

    public string DriverName { get; set; } = string.Empty;

    public string Car { get; set; } = string.Empty;

    public string Skin { get; set; } = string.Empty;

    public string CarDisplayName { get; set; } = string.Empty;

    public string? TeamName { get; set; }

    public string ClassName { get; set; } = string.Empty;

    public string ClassColor { get; set; } = RaceFormat.UnknownClassColor;

    public int LapCount { get; set; }

    public int BestLapTimeMs { get; set; }

    /// <summary>Where the car qualified, when the race was preceded by a qualifying session.</summary>
    public int? QualifyingPosition { get; set; }

    public int? QualifyingTimeMs { get; set; }

    public bool IsHuman { get; set; }
}

public class QuickRaceResultModel
{
    public Guid ResultId { get; set; }

    public DateTime RaceDate { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? TrackName { get; set; }

    public string? TrackLayout { get; set; }

    public int DriverCount { get; set; }

    public int? HumanPosition { get; set; }

    public int? HumanClassPosition { get; set; }

    public string? HumanClassName { get; set; }

    public List<QuickRaceResultRow> Rows { get; set; } = new();
}

/// <summary>A line of the history list: when the race was and where the human finished.</summary>
public sealed record QuickRaceHistoryItem(
    Guid ResultId,
    DateTime RaceDate,
    string? TrackName,
    string? TrackLayout,
    int DriverCount,
    int? HumanPosition,
    int? HumanClassPosition,
    string? HumanClassName);

/// <summary>The outcome of a quick race qualifying session: every car in qualifying order.</summary>
public class QuickRaceQualifyingModel
{
    public DateTime Date { get; set; }

    public List<QuickRaceResultRow> Rows { get; set; } = new();
}
