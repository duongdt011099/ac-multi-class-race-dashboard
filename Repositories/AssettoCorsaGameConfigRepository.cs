using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class AssettoCorsaGameConfigRepository : GenericRepository<AssettoCorsaGameConfig>, IAssettoCorsaGameConfigRepository
{
    public AssettoCorsaGameConfigRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<AssettoCorsaGameConfig?> GetAsync()
    {
        return await _dbSet.FirstOrDefaultAsync();
    }
}