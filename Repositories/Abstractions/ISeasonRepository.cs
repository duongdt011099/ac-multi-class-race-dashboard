using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface ISeasonRepository : IGenericRepository<Season>
{
    Task<IEnumerable<Season>> GetSeasonsByChampionshipAsync(Guid championshipId);
}