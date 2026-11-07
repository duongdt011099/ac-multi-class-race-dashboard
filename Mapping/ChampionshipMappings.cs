using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Mapping;

public static class ChampionshipMappings
{
    public static ChampionshipModel ToModel(this Championship championship)
    {
        return new ChampionshipModel
        {
            ChampionshipId = championship.ChampionshipId,
            ChampionshipName = championship.ChampionshipName,
            SeasonCount = championship.Seasons.Count,
            IsActive = championship.IsActive
        };
    }

    public static ChampionshipFormModel ToFormModel(this Championship championship)
    {
        return new ChampionshipFormModel
        {
            ChampionshipId = championship.ChampionshipId,
            ChampionshipName = championship.ChampionshipName,
            IsActive = championship.IsActive
        };
    }

    public static SeasonModel ToModel(this Season season)
    {
        return new SeasonModel
        {
            SeasonId = season.SeasonId,
            SeasonName = season.SeasonName,
            RaceCount = season.Races.Count,
            NumberOfRounds = season.NumberOfRounds,
            FinishedRaceCount = season.Races.Count(r => r.Status == RaceStatus.Finished),
            IsFinished = season.IsFinished,
            ClassSettingId = season.ClassSettingId,
            ClassSettingName = season.ClassSetting?.ClassSettingName
        };
    }
}

public static class AchievementMappings
{
    public static AchievementModel ToModel(this Achievement achievement)
    {
        return new AchievementModel
        {
            AchievementId = achievement.AchievementId,
            Type = achievement.Type,
            DriverName = achievement.DriverName,
            ChampionshipName = achievement.ChampionshipName,
            SeasonName = achievement.SeasonName,
            RaceName = achievement.RaceName,
            TrackName = achievement.TrackName,
            ClassName = achievement.ClassName,
            Points = achievement.Points,
            Position = achievement.Position,
            EarnedAt = achievement.EarnedAt
        };
    }
}
