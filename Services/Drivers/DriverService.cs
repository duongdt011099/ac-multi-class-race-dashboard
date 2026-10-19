using System.Text.Json;
using MulticlassRace.Mapping;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.AssettoCorsa;

namespace MulticlassRace.Services.Drivers;

public class DriverService : IDriverService
{
    private const long MaxPresetSizeBytes = 2 * 1024 * 1024;

    private readonly IDriverRepository _driverRepository;
    private readonly AssettoCorsaPathResolver _pathResolver;
    private readonly ITeamRepository _teamRepository;

    public DriverService(
        IDriverRepository driverRepository,
        AssettoCorsaPathResolver pathResolver,
        ITeamRepository teamRepository)
    {
        _driverRepository = driverRepository;
        _pathResolver = pathResolver;
        _teamRepository = teamRepository;
    }

    public async Task<IEnumerable<DriverModel>> SearchAvailableDriversAsync(string? searchTerm)
    {
        var drivers = await _driverRepository.SearchUnassignedDriversAsync(searchTerm);
        var gamePath = await GetGamePathAsync();

        return drivers.Select(d => d.ToModel(gamePath));
    }

    public async Task<IEnumerable<DriverModel>> GetActiveDriversAsync()
    {
        var drivers = await _driverRepository.GetActiveDriversAsync();
        var gamePath = await GetGamePathAsync();

        return drivers.Select(d => d.ToModel(gamePath));
    }

    public async Task<DriverFormModel?> GetDriverByIdAsync(Guid driverId)
    {
        var driver = await _driverRepository.GetDriverWithDetailsAsync(driverId);

        return driver?.ToFormModel();
    }

    public async Task CreateDriverAsync(DriverFormModel model)
    {
        if (model.IsHuman)
        {
            await _driverRepository.UnsetHumanFlagAsync();
        }

        var driver = new Driver
        {
            DriverId = Guid.NewGuid(),
            DriverName = model.DriverName.Trim(),
            CarDisplayName = model.CarDisplayName.Trim(),
            Car = model.Car.Trim(),
            Skin = model.Skin.Trim(),
            Nationality = string.IsNullOrWhiteSpace(model.Nationality) ? null : model.Nationality.Trim(),
            Age = model.Age,
            DriverAgression = model.DriverAgression,
            DriverStrength = model.DriverStrength,
            Team = model.TeamId is { } teamId ? await _teamRepository.GetByIdAsync(teamId) : null,
            IsActive = model.IsActive,
            IsHuman = model.IsHuman
        };

        await _driverRepository.AddAsync(driver);
    }

    public async Task UpdateDriverAsync(DriverFormModel model)
    {
        var driver = await _driverRepository.GetDriverWithDetailsAsync(model.DriverId);

        if (driver is null)
        {
            return;
        }

        if (model.IsHuman)
        {
            await _driverRepository.UnsetHumanFlagAsync(model.DriverId);
        }

        driver.DriverName = model.DriverName.Trim();
        driver.CarDisplayName = model.CarDisplayName.Trim();
        driver.Car = model.Car.Trim();
        driver.Skin = model.Skin.Trim();
        driver.Nationality = string.IsNullOrWhiteSpace(model.Nationality) ? null : model.Nationality.Trim();
        driver.Age = model.Age;
        driver.DriverAgression = model.DriverAgression;
        driver.DriverStrength = model.DriverStrength;
        driver.Team = model.TeamId is { } teamId ? await _teamRepository.GetByIdAsync(teamId) : null;
        driver.IsActive = model.IsActive;
        driver.IsHuman = model.IsHuman;

        await _driverRepository.UpdateAsync(driver);
    }

    public async Task BatchUpdateAiAsync(IReadOnlyCollection<Guid> driverIds, int? aggression, int? strength)
    {
        if (driverIds.Count == 0 || (aggression is null && strength is null))
        {
            return;
        }

        await _driverRepository.UpdateAiAsync(driverIds, aggression, strength);
    }

    public async Task DeleteDriverAsync(Guid driverId)
    {
        await _driverRepository.DeleteAsync(driverId);
    }

    public async Task<PresetImportResult> ImportAssettoCorsaPresetAsync(Stream presetStream, string presetName)
    {
        AcGridPreset? preset;

        try
        {
            preset = await JsonSerializer.DeserializeAsync<AcGridPreset>(presetStream);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "The selected file is not a valid Assetto Corsa grid preset.",
                ex);
        }

        if (preset is null || preset.CarIds.Count == 0 || preset.SkinIds.Count == 0)
        {
            throw new InvalidOperationException("The preset contains no cars or skins to import.");
        }

        var count = Math.Min(preset.CarIds.Count, preset.SkinIds.Count);
        var drivers = new List<Driver>(count);

        for (var i = 0; i < count; i++)
        {
            var car = preset.CarIds[i]?.Trim() ?? string.Empty;
            var skin = preset.SkinIds[i]?.Trim() ?? string.Empty;
            var name = i < preset.Names.Count ? preset.Names[i]?.Trim() : null;

            drivers.Add(new Driver
            {
                DriverId = Guid.NewGuid(),
                DriverName = string.IsNullOrWhiteSpace(name) ? "unknown" : name,
                CarDisplayName = car,
                Car = car,
                Skin = skin,
                DriverAgression = ParseAiValue(preset.AiAggressions, i),
                DriverStrength = ParseAiValue(preset.AiLevels, i),
                IsActive = true
            });
        }

        var added = await _driverRepository.AddMissingAsync(drivers);

        return new PresetImportResult
        {
            PresetName = presetName,
            Added = added,
            Skipped = drivers.Count - added
        };
    }

    public async Task<PresetImportResult> ImportAssettoCorsaPresetFileAsync(string presetFilePath)
    {
        if (string.IsNullOrWhiteSpace(presetFilePath) || !File.Exists(presetFilePath))
        {
            throw new InvalidOperationException("The selected preset file no longer exists.");
        }

        var file = new FileInfo(presetFilePath);

        if (file.Length > MaxPresetSizeBytes)
        {
            throw new InvalidOperationException("Preset file must be 2MB or smaller.");
        }

        await using var stream = file.OpenRead();
        return await ImportAssettoCorsaPresetAsync(stream, file.Name);
    }

    private async Task<string?> GetGamePathAsync()
    {
        return (await _pathResolver.GetAsync()).Path;
    }

    private static int? ParseAiValue(List<string> values, int index)
    {
        if (index >= values.Count || !int.TryParse(values[index]?.Trim(), out var value))
        {
            return null;
        }

        return value < 0 ? null : value;
    }

    private sealed class AcGridPreset
    {
        public List<string> CarIds { get; set; } = new();

        public List<string> SkinIds { get; set; } = new();

        public List<string?> Names { get; set; } = new();

        public List<string> AiLevels { get; set; } = new();

        public List<string> AiAggressions { get; set; } = new();
    }
}