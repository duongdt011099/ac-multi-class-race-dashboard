using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface IAssettoCorsaGameConfigRepository : IGenericRepository<AssettoCorsaGameConfig>
{
    Task<AssettoCorsaGameConfig?> GetAsync();
}