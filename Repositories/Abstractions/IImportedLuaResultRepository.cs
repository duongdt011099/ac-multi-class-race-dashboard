using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface IImportedLuaResultRepository : IGenericRepository<ImportedLuaResult>
{
    Task<ImportedLuaResult?> FindByFullPathAsync(string fullPath);

    Task<ImportedLuaResult?> FindByFileNameAsync(string fileName, SessionType sessionType);
}
