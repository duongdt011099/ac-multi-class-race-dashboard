using MulticlassRace.Mapping;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class ClassSettingService : IClassSettingService
{
    private readonly IClassSettingRepository _classSettingRepository;

    public ClassSettingService(IClassSettingRepository classSettingRepository)
    {
        _classSettingRepository = classSettingRepository;
    }

    public async Task<IEnumerable<ClassSettingModel>> GetAllAsync()
    {
        var settings = await _classSettingRepository.GetAllOrderedAsync();

        return settings.Select(s => s.ToModel());
    }

    public async Task<ClassSettingModel?> GetDefaultAsync()
    {
        var setting = await _classSettingRepository.GetDefaultAsync();

        return setting?.ToModel();
    }

    public async Task CreateAsync(ClassSettingFormModel model)
    {
        var items = BuildItems(model);

        var setting = new ClassSetting
        {
            ClassSettingId = Guid.NewGuid(),
            ClassSettingName = model.ClassSettingName.Trim(),
            IsDefault = model.IsDefault,
            Items = items.Select(i => new ClassSettingItem
            {
                ClassSettingItemId = Guid.NewGuid(),
                Name = i.Name.Trim(),
                Order = i.Order,
                Color = NormalizeColor(i.Color)
            }).ToList()
        };

        if (setting.IsDefault)
        {
            await ClearOtherDefaultsAsync(setting.ClassSettingId);
        }
        else if (!(await _classSettingRepository.GetAllOrderedAsync()).Any(s => s.IsDefault))
        {
            setting.IsDefault = true;
        }

        await _classSettingRepository.AddAsync(setting);
    }

    public async Task UpdateAsync(ClassSettingFormModel model)
    {
        var setting = await _classSettingRepository.GetByIdWithItemsAsync(model.ClassSettingId);

        if (setting is null)
        {
            throw new InvalidOperationException("Class setting not found.");
        }

        var wasDefault = setting.IsDefault;

        var items = BuildItems(model);
        var keepIds = items
            .Select(i => i.ClassSettingItemId)
            .Where(i => i != Guid.Empty)
            .ToHashSet();

        foreach (var existing in setting.Items.Where(i => !keepIds.Contains(i.ClassSettingItemId)).ToList())
        {
            setting.Items.Remove(existing);
        }

        foreach (var item in items)
        {
            if (item.ClassSettingItemId != Guid.Empty &&
                setting.Items.FirstOrDefault(i => i.ClassSettingItemId == item.ClassSettingItemId) is { } existing)
            {
                existing.Name = item.Name.Trim();
                existing.Order = item.Order;
                existing.Color = NormalizeColor(item.Color);
            }
            else
            {
                setting.Items.Add(new ClassSettingItem
                {
                    ClassSettingItemId = Guid.NewGuid(),
                    Name = item.Name.Trim(),
                    Order = item.Order,
                    Color = NormalizeColor(item.Color)
                });
            }
        }

        setting.ClassSettingName = model.ClassSettingName.Trim();
        setting.IsDefault = model.IsDefault;

        if (setting.IsDefault)
        {
            await ClearOtherDefaultsAsync(setting.ClassSettingId);
        }
        else if (wasDefault &&
                 !(await _classSettingRepository.GetAllOrderedAsync())
                     .Any(s => s.IsDefault && s.ClassSettingId != setting.ClassSettingId))
        {
            throw new InvalidOperationException("At least one class setting must remain the default.");
        }

        await _classSettingRepository.UpdateAsync(setting);
    }

    public async Task DeleteAsync(Guid classSettingId)
    {
        var setting = await _classSettingRepository.GetByIdAsync(classSettingId);

        if (setting is null)
        {
            throw new InvalidOperationException("Class setting not found.");
        }

        if (setting.IsDefault)
        {
            throw new InvalidOperationException("The default class setting cannot be deleted.");
        }

        var raceCount = await _classSettingRepository.CountRacesUsingAsync(classSettingId);
        var teamCount = await _classSettingRepository.CountTeamsUsingAsync(classSettingId);

        if (raceCount > 0 || teamCount > 0)
        {
            throw new InvalidOperationException("This class setting is in use and cannot be deleted.");
        }

        await _classSettingRepository.DeleteAsync(classSettingId);
    }

    private async Task ClearOtherDefaultsAsync(Guid exceptSettingId)
    {
        var settings = (await _classSettingRepository.GetAllOrderedAsync())
            .Where(s => s.IsDefault && s.ClassSettingId != exceptSettingId)
            .ToList();

        foreach (var setting in settings)
        {
            setting.IsDefault = false;
        }
    }

    private static List<ClassSettingItemFormModel> BuildItems(ClassSettingFormModel model)
    {
        var items = model.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .ToList();

        if (items.Count == 0)
        {
            throw new InvalidOperationException("Add at least one class item.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < items.Count; i++)
        {
            var name = items[i].Name.Trim();

            if (!seen.Add(name))
            {
                throw new InvalidOperationException($"Class item '{name}' is entered more than once.");
            }

            items[i].Name = name;
            items[i].Order = i + 1;
        }

        return items;
    }

    private static string NormalizeColor(string color)
    {
        return string.IsNullOrWhiteSpace(color) ? "#52525b" : color.Trim();
    }
}
