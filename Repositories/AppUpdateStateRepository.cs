using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class AppUpdateStateRepository : GenericRepository<AppUpdateState>, IAppUpdateStateRepository
{
    public AppUpdateStateRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<AppUpdateState?> GetAsync()
    {
        return await _dbSet.FirstOrDefaultAsync();
    }
}
