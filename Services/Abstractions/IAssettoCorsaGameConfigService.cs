using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Abstractions;

public interface IAssettoCorsaGameConfigService
{
    Task<AssettoCorsaGameConfigModel?> GetAsync();
    Task<bool> SaveAsync(AssettoCorsaGameConfigModel model);
}