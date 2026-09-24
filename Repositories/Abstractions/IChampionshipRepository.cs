using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface IChampionshipRepository : IGenericRepository<Championship>
{
    Task<IEnumerable<Championship>> GetActiveChampionshipsAsync();
    Task DeacivateChampionshipAsync(Guid championshipId);
}