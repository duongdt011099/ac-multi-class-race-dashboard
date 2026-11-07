using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface ISeasonRepository : IGenericRepository<Season>
{
    Task<IEnumerable<Season>> GetSeasonsByChampionshipAsync(Guid championshipId);
    Task<Season?> GetSeasonWithRacesAsync(Guid seasonId);
    Task<Season?> GetSeasonForCompletionAsync(Guid seasonId);
}