using System.Text.Json;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class DriverService : IDriverService
{
    private const long MaxPresetSizeBytes = 2 * 1024 * 1024;

    private readonly IDriverRepository _driverRepository;
    private readonly IAssettoCorsaGameConfigRepository _configRepository;
    private readonly ITeamRepository _teamRepository;
    private string? _gamePath;

    public DriverService(IDriverRepository driverRepository, IAssettoCorsaGameConfigRepository configRepository, ITeamRepository teamRepository)
    {
        _driverRepository = driverRepository;
        _configRepository = configRepository;
        _teamRepository = teamRepository;
    }

    public async Task<IEnumerable<DriverModel>> SearchAvailableDriversAsync(string? searchTerm)
    {
        var drivers = await _driverRepository.SearchUnassignedDriversAsync(searchTerm);
        var gamePath = await GetGamePathAsync();

        return drivers.Select(d => ToModel(d, gamePath));
    }

    public async Task<IEnumerable<DriverModel>> GetActiveDriversAsync()
    {
        var drivers = await _driverRepository.GetActiveDriversAsync();
        var gamePath = await GetGamePathAsync();

        return drivers.Select(d => ToModel(d, gamePath));
    }

    public async Task<DriverFormModel?> GetDriverByIdAsync(Guid driverId)
    {
        var driver = await _driverRepository.GetByIdAsync(driverId);

        return driver is null ? null : ToFormModel(driver);
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
        var driver = await _driverRepository.GetByIdAsync(model.DriverId);

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
        if (_gamePath is not null)
        {
            return _gamePath;
        }

        var config = await _configRepository.GetAsync();
        _gamePath = config?.GamePath.Trim();

        return string.IsNullOrWhiteSpace(_gamePath) ? null : _gamePath;
    }

    private static int? ParseAiValue(List<string> values, int index)
    {
        if (index >= values.Count || !int.TryParse(values[index]?.Trim(), out var value))
        {
            return null;
        }

        return value < 0 ? null : value;
    }

    private static DriverModel ToModel(Driver driver, string? gamePath)
    {
        return new DriverModel
        {
            DriverId = driver.DriverId,
            DriverName = driver.DriverName,
            CarDisplayName = driver.CarDisplayName,
            Car = driver.Car,
            Skin = driver.Skin,
            LiveryImageUrl = BuildImageUrl(gamePath, driver.Car, driver.Skin, "livery"),
            CarImageUrl = BuildImageUrl(gamePath, driver.Car, driver.Skin, "car"),
            Nationality = driver.Nationality,
            Age = driver.Age,
            DriverAgression = driver.DriverAgression,
            DriverStrength = driver.DriverStrength,
            TeamName = driver.Team?.TeamName,
            TeamId = driver.Team?.TeamId,
            TeamLogo = driver.Team?.TeamLogo,
            TeamClassName = driver.Team?.TeamClass?.TeamClassName,
            IsActive = driver.IsActive,
            IsHuman = driver.IsHuman
        };
    }

    private static DriverFormModel ToFormModel(Driver driver)
    {
        return new DriverFormModel
        {
            DriverId = driver.DriverId,
            DriverName = driver.DriverName,
            CarDisplayName = driver.CarDisplayName,
            Car = driver.Car,
            Skin = driver.Skin,
            Nationality = driver.Nationality,
            Age = driver.Age,
            DriverAgression = driver.DriverAgression,
            DriverStrength = driver.DriverStrength,
            TeamId = driver.Team?.TeamId,
            TeamName = driver.Team?.TeamName,
            IsActive = driver.IsActive,
            IsHuman = driver.IsHuman
        };
    }

    private static string? BuildImageUrl(string? gamePath, string car, string skin, string type)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(car) || string.IsNullOrWhiteSpace(skin))
        {
            return null;
        }

        return $"/car-image?car={Uri.EscapeDataString(car)}&skin={Uri.EscapeDataString(skin)}&type={type}";
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