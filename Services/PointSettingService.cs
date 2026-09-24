using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class PointSettingService : IPointSettingService
{
    private readonly IPointSettingRepository _pointSettingRepository;

    public PointSettingService(IPointSettingRepository pointSettingRepository)
    {
        _pointSettingRepository = pointSettingRepository;
    }

    public async Task<IEnumerable<PointSettingModel>> GetAllAsync()
    {
        var settings = await _pointSettingRepository.GetAllOrderedAsync();

        return settings.Select(s => new PointSettingModel
        {
            SettingId = s.SettingId,
            SettingName = s.SettingName,
            IsDefault = s.IsDefault,
            Config = s.Config
        });
    }

    public async Task CreateAsync(PointSettingFormModel model)
    {
        var setting = new PointSetting
        {
            SettingId = Guid.NewGuid(),
            SettingName = model.SettingName.Trim(),
            IsDefault = model.IsDefault,
            Config = BuildConfig(model.Entries)
        };

        if (setting.IsDefault)
        {
            await ClearOtherDefaultsAsync(setting.SettingId);
        }

        await _pointSettingRepository.AddAsync(setting);
    }

    public async Task UpdateAsync(PointSettingFormModel model)
    {
        var setting = await _pointSettingRepository.GetByIdAsync(model.SettingId);

        if (setting is null)
        {
            throw new InvalidOperationException("Point setting not found.");
        }

        setting.SettingName = model.SettingName.Trim();
        setting.IsDefault = model.IsDefault;
        setting.Config = BuildConfig(model.Entries);

        if (setting.IsDefault)
        {
            await ClearOtherDefaultsAsync(setting.SettingId);
        }

        await _pointSettingRepository.UpdateAsync(setting);
    }

    public async Task DeleteAsync(Guid pointSettingId)
    {
        var racesUsing = await _pointSettingRepository.CountRacesUsingAsync(pointSettingId);

        if (racesUsing > 0)
        {
            throw new InvalidOperationException("This point setting is used by a race and cannot be deleted.");
        }

        await _pointSettingRepository.DeleteAsync(pointSettingId);
    }

    private async Task ClearOtherDefaultsAsync(Guid exceptSettingId)
    {
        var settings = (await _pointSettingRepository.GetAllOrderedAsync())
            .Where(s => s.IsDefault && s.SettingId != exceptSettingId)
            .ToList();

        if (settings.Count == 0)
        {
            return;
        }

        foreach (var setting in settings)
        {
            setting.IsDefault = false;
        }

        foreach (var setting in settings)
        {
            await _pointSettingRepository.UpdateAsync(setting);
        }
    }

    private static Dictionary<string, int> BuildConfig(IEnumerable<PointSettingEntryModel> entries)
    {
        var config = new Dictionary<string, int>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Position) && string.IsNullOrWhiteSpace(entry.Points))
            {
                continue;
            }

            if (!int.TryParse(entry.Position, out var position) || position < 1)
            {
                throw new InvalidOperationException("Each row needs a valid Position (number, min 1).");
            }

            if (!int.TryParse(entry.Points, out var points))
            {
                throw new InvalidOperationException($"Points for position {position} must be a number.");
            }

            if (config.ContainsKey(position.ToString()))
            {
                throw new InvalidOperationException($"Position {position} is entered more than once.");
            }

            config[position.ToString()] = points;
        }

        if (config.Count == 0)
        {
            throw new InvalidOperationException("Add at least one position / points row.");
        }

        return config
            .OrderBy(kv => int.Parse(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}