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
            RaceCount = season.Races.Count
        };
    }
}
