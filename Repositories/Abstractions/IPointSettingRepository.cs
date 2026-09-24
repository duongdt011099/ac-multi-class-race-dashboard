using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface IPointSettingRepository : IGenericRepository<PointSetting>
{
    Task<IEnumerable<PointSetting>> GetAllOrderedAsync();
    Task<int> CountRacesUsingAsync(Guid pointSettingId);
}