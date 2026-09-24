using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class PointSettingRepository : GenericRepository<PointSetting>, IPointSettingRepository
{
    public PointSettingRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<PointSetting>> GetAllOrderedAsync()
    {
        return await _dbSet
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.SettingName)
            .ToListAsync();
    }

    public async Task<int> CountRacesUsingAsync(Guid pointSettingId)
    {
        return await _context.Races.CountAsync(r => r.PointSettingId == pointSettingId);
    }
}