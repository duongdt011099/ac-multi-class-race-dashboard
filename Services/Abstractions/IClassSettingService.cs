using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Abstractions;

public interface IClassSettingService
{
    Task<IEnumerable<ClassSettingModel>> GetAllAsync();

    Task<ClassSettingModel?> GetDefaultAsync();

    Task CreateAsync(ClassSettingFormModel model);

    Task UpdateAsync(ClassSettingFormModel model);

    Task DeleteAsync(Guid classSettingId);
}