using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.AssettoCorsa;

public interface IAssettoCorsaGameConfigService
{
    Task<AssettoCorsaGameConfigModel?> GetAsync();
    Task<bool> SaveAsync(AssettoCorsaGameConfigModel model);
}