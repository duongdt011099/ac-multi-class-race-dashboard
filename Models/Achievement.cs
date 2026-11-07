namespace MulticlassRace.Models;

public enum AchievementType
{
    /// <summary>Won a race in the human driver's class.</summary>
    RaceWin,

    /// <summary>Finished a completed season first in the human driver's class: the world championship.</summary>
    SeasonChampion,

    /// <summary>Finished a completed season second in class.</summary>
    SeasonRunnerUp,

    /// <summary>Finished a completed season third in class.</summary>
    SeasonThird
}

/// <summary>A trophy earned by the human driver. Names are copied so the trophy still reads right if the driver is renamed or removed.</summary>
public class Achievement
{
    public Guid AchievementId { get; set; }

    public AchievementType Type { get; set; }

    public Guid DriverId { get; set; }

    public string DriverName { get; set; } = string.Empty;

    /// <summary>Deleting the season removes its trophies.</summary>
    public Guid SeasonId { get; set; }

    /// <summary>Set for race wins; deleting the race removes the trophy.</summary>
    public Guid? RaceId { get; set; }

    public string ChampionshipName { get; set; } = string.Empty;

    public string SeasonName { get; set; } = string.Empty;

    public string? RaceName { get; set; }

    public string? TrackName { get; set; }

    public string? ClassName { get; set; }

    /// <summary>Season trophies: the season's final points in class. Race wins: the points scored.</summary>
    public int Points { get; set; }

    public int Position { get; set; } = 1;

    public DateTime EarnedAt { get; set; }
}
