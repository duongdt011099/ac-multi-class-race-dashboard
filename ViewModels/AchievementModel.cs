using MulticlassRace.Models;

namespace MulticlassRace.ViewModels;

public enum TrophyTier
{
    Gold,
    Silver,
    Bronze,
    Diamond
}

public class AchievementModel
{
    public Guid AchievementId { get; set; }

    public AchievementType Type { get; set; }

    public string DriverName { get; set; } = string.Empty;

    public string ChampionshipName { get; set; } = string.Empty;

    public string SeasonName { get; set; } = string.Empty;

    public string? RaceName { get; set; }

    public string? TrackName { get; set; }

    public string? ClassName { get; set; }

    public int Points { get; set; }

    public int Position { get; set; }

    public DateTime EarnedAt { get; set; }

    public bool IsSeasonTrophy => Type != AchievementType.RaceWin;

    public bool IsChampion => Type == AchievementType.SeasonChampion;

    public TrophyTier Tier => Type switch
    {
        AchievementType.SeasonChampion => TrophyTier.Diamond,
        AchievementType.SeasonRunnerUp => TrophyTier.Silver,
        AchievementType.SeasonThird => TrophyTier.Bronze,
        _ => TrophyTier.Gold
    };

    /// <summary>The word engraved on the cup.</summary>
    public string Badge => Type switch
    {
        AchievementType.SeasonChampion => "World Champion",
        AchievementType.SeasonRunnerUp => "Runner-up",
        AchievementType.SeasonThird => "Third place",
        _ => "Race winner"
    };

    /// <summary>The main text engraved on the cup: the race for a win, the season for a season trophy.</summary>
    public string Engraving => Type == AchievementType.RaceWin
        ? (string.IsNullOrWhiteSpace(RaceName) ? SeasonName : RaceName)
        : SeasonName;

    public string TierImageUrl => $"images/trophies/{Tier.ToString().ToLowerInvariant()}.svg";

    public string Description => Type switch
    {
        AchievementType.SeasonChampion => $"Won the {SeasonName} world championship",
        AchievementType.SeasonRunnerUp => $"Second in the {SeasonName} championship",
        AchievementType.SeasonThird => $"Third in the {SeasonName} championship",
        _ => $"Won {Engraving} in {SeasonName}"
    };
}
