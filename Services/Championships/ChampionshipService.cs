using MulticlassRace.Mapping;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Championships;

public class ChampionshipService : IChampionshipService
{
    private readonly IChampionshipRepository _championshipRepository;

    public ChampionshipService(IChampionshipRepository championshipRepository)
    {
        _championshipRepository = championshipRepository;
    }

    public async Task<IEnumerable<ChampionshipModel>> GetActiveChampionshipsAsync()
    {
        var championships = await _championshipRepository.GetActiveChampionshipsAsync();

        return championships.Select(c => c.ToModel());
    }

    public async Task<ChampionshipFormModel?> GetChampionshipByIdAsync(Guid championshipId)
    {
        var championship = await _championshipRepository.GetByIdAsync(championshipId);

        return championship?.ToFormModel();
    }

    public async Task CreateChampionshipAsync(ChampionshipFormModel model)
    {
        var championship = new Championship
        {
            ChampionshipId = Guid.NewGuid(),
            ChampionshipName = model.ChampionshipName.Trim(),
            IsActive = model.IsActive
        };

        await _championshipRepository.AddAsync(championship);
    }

    public async Task UpdateChampionshipAsync(ChampionshipFormModel model)
    {
        var championship = await _championshipRepository.GetByIdAsync(model.ChampionshipId);

        if (championship is null)
        {
            return;
        }

        championship.ChampionshipName = model.ChampionshipName.Trim();
        championship.IsActive = model.IsActive;

        await _championshipRepository.UpdateAsync(championship);
    }

    public async Task DeleteChampionshipAsync(Guid championshipId)
    {
        await _championshipRepository.DeleteAsync(championshipId);
    }
}