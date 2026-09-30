using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class ImportedLuaResultRepository : GenericRepository<ImportedLuaResult>, IImportedLuaResultRepository
{
    public ImportedLuaResultRepository(AppDbContext context) : base(context)
    {
    }

    public Task<ImportedLuaResult?> FindByFullPathAsync(string fullPath)
    {
        var normalized = fullPath.Trim();

        return _dbSet.FirstOrDefaultAsync(r => r.FullPath == normalized);
    }

    public Task<ImportedLuaResult?> FindByFileNameAsync(string fileName, SessionType sessionType)
    {
        var normalized = fileName.Trim();

        return _dbSet.FirstOrDefaultAsync(r => r.FileName == normalized && r.SessionType == sessionType);
    }
}
