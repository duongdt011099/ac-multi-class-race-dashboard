using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Abstractions;

public interface ISeasonService
{
    Task<IEnumerable<SeasonModel>> GetSeasonsByChampionshipAsync(Guid championshipId);
    Task CreateSeasonAsync(Guid championshipId, string seasonName);
    Task DeleteSeasonAsync(Guid seasonId);
    Task<IEnumerable<DriverStandingsModel>> GetDriverStandingsAsync(Guid seasonId);
}