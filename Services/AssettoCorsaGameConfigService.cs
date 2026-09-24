using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class AssettoCorsaGameConfigService : IAssettoCorsaGameConfigService
{
    private readonly IAssettoCorsaGameConfigRepository _configRepository;

    public AssettoCorsaGameConfigService(IAssettoCorsaGameConfigRepository configRepository)
    {
        _configRepository = configRepository;
    }

    public async Task<AssettoCorsaGameConfigModel?> GetAsync()
    {
        var config = await _configRepository.GetAsync();

        if (config is null)
        {
            return null;
        }

        return new AssettoCorsaGameConfigModel
        {
            GamePath = config.GamePath,
            PresetPath = config.PresetPath,
            RaceResultsPath = config.RaceResultsPath
        };
    }

    public async Task<bool> SaveAsync(AssettoCorsaGameConfigModel model)
    {
        var config = await _configRepository.GetAsync();

        if (config is null)
        {
            var newConfig = new AssettoCorsaGameConfig
            {
                AssettoCorsaGameConfigId = Guid.NewGuid(),
                GamePath = model.GamePath.Trim(),
                PresetPath = model.PresetPath.Trim(),
                RaceResultsPath = model.RaceResultsPath.Trim()
            };

            await _configRepository.AddAsync(newConfig);
            return true;
        }

        config.GamePath = model.GamePath.Trim();
        config.PresetPath = model.PresetPath.Trim();
        config.RaceResultsPath = model.RaceResultsPath.Trim();

        await _configRepository.UpdateAsync(config);
        return false;
    }
}