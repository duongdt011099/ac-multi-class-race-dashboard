using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class SeasonService : ISeasonService
{
    private readonly ISeasonRepository _seasonRepository;
    private readonly IChampionshipRepository _championshipRepository;

    public SeasonService(ISeasonRepository seasonRepository, IChampionshipRepository championshipRepository)
    {
        _seasonRepository = seasonRepository;
        _championshipRepository = championshipRepository;
    }

    public async Task<IEnumerable<SeasonModel>> GetSeasonsByChampionshipAsync(Guid championshipId)
    {
        var seasons = await _seasonRepository.GetSeasonsByChampionshipAsync(championshipId);

        return seasons.Select(s => new SeasonModel
        {
            SeasonId = s.SeasonId,
            SeasonName = s.SeasonName,
            RaceCount = s.Races.Count
        });
    }

    public async Task CreateSeasonAsync(Guid championshipId, string seasonName)
    {
        var championship = await _championshipRepository.GetByIdAsync(championshipId);

        if (championship is null)
        {
            throw new InvalidOperationException("Championship not found.");
        }

        var season = new Season
        {
            SeasonId = Guid.NewGuid(),
            SeasonName = seasonName.Trim(),
            Championship = championship
        };

        await _seasonRepository.AddAsync(season);
    }

    public async Task DeleteSeasonAsync(Guid seasonId)
    {
        await _seasonRepository.DeleteAsync(seasonId);
    }
}