using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Abstractions;

public interface IPointSettingService
{
    Task<IEnumerable<PointSettingModel>> GetAllAsync();
    Task CreateAsync(PointSettingFormModel model);
    Task UpdateAsync(PointSettingFormModel model);
    Task DeleteAsync(Guid pointSettingId);
}