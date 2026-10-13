using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Championships;

public interface IChampionshipService
{
    Task<IEnumerable<ChampionshipModel>> GetActiveChampionshipsAsync();
    Task<ChampionshipFormModel?> GetChampionshipByIdAsync(Guid championshipId);
    Task CreateChampionshipAsync(ChampionshipFormModel model);
    Task UpdateChampionshipAsync(ChampionshipFormModel model);
    Task DeleteChampionshipAsync(Guid championshipId);
}