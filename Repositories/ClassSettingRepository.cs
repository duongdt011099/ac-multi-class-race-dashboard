using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class ClassSettingRepository : GenericRepository<ClassSetting>, IClassSettingRepository
{
    public ClassSettingRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<ClassSetting>> GetAllOrderedAsync()
    {
        return await _dbSet
            .Include(c => c.Items)
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.ClassSettingName)
            .ToListAsync();
    }

    public async Task<ClassSetting?> GetDefaultAsync()
    {
        return await _dbSet
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.IsDefault);
    }

    public async Task<ClassSetting?> GetByIdWithItemsAsync(Guid classSettingId)
    {
        return await _dbSet
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.ClassSettingId == classSettingId);
    }

    public async Task<IEnumerable<ClassSettingItem>> GetItemsAsync(IEnumerable<Guid> itemIds)
    {
        var ids = itemIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return new List<ClassSettingItem>();
        }

        return await _context.ClassSettingItems
            .Include(i => i.ClassSetting)
            .Where(i => ids.Contains(i.ClassSettingItemId))
            .ToListAsync();
    }

    public async Task<int> CountRacesUsingAsync(Guid classSettingId)
    {
        return await _context.Races.CountAsync(r => r.ClassSettingId == classSettingId);
    }

    public async Task<int> CountTeamsUsingAsync(Guid classSettingId)
    {
        return await _context.Teams
            .Where(t => t.ClassSettingItems.Any(i => i.ClassSettingId == classSettingId))
            .CountAsync();
    }
}