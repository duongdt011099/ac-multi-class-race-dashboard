using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface IClassSettingRepository : IGenericRepository<ClassSetting>
{
    Task<IEnumerable<ClassSetting>> GetAllOrderedAsync();

    Task<ClassSetting?> GetDefaultAsync();

    Task<ClassSetting?> GetByIdWithItemsAsync(Guid classSettingId);

    Task<IEnumerable<ClassSettingItem>> GetItemsAsync(IEnumerable<Guid> itemIds);

    Task<int> CountRacesUsingAsync(Guid classSettingId);

    Task<int> CountTeamsUsingAsync(Guid classSettingId);
}