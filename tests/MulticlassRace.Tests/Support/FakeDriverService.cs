using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.Support;

/// <summary>In-memory driver list, so page tests do not depend on the machine's Assetto Corsa install.</summary>
public sealed class FakeDriverService : IDriverService
{
    public List<DriverModel> Drivers { get; } = new();

    public List<DriverFormModel> Saved { get; } = new();

    public List<Guid> Deleted { get; } = new();

    public static DriverModel Driver(string name, Guid? teamId = null, string? teamName = null, params ClassTagModel[] tags)
    {
        return new DriverModel
        {
            DriverId = Guid.NewGuid(),
            DriverName = name,
            Car = "car",
            CarDisplayName = "Car",
            Skin = "skin",
            TeamId = teamId,
            TeamName = teamName,
            ClassTags = tags.ToList()
        };
    }

    public Task<IEnumerable<DriverModel>> GetActiveDriversAsync() => Task.FromResult<IEnumerable<DriverModel>>(Drivers.ToList());

    public Task<IEnumerable<DriverModel>> SearchAvailableDriversAsync(string? searchTerm)
    {
        var results = Drivers
            .Where(d => d.TeamId is null)
            .Where(d => string.IsNullOrWhiteSpace(searchTerm) ||
                        d.DriverName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IEnumerable<DriverModel>>(results.ToList());
    }

    public Task<DriverFormModel?> GetDriverByIdAsync(Guid driverId) => throw new NotSupportedException();

    public Task CreateDriverAsync(DriverFormModel model) => Store(model);

    public Task UpdateDriverAsync(DriverFormModel model)
    {
        Drivers.RemoveAll(d => d.DriverId == model.DriverId);
        return Store(model);
    }

    public Task DeleteDriverAsync(Guid driverId)
    {
        Deleted.Add(driverId);
        Drivers.RemoveAll(d => d.DriverId == driverId);
        return Task.CompletedTask;
    }

    public Task<PresetImportResult> ImportAssettoCorsaPresetAsync(Stream presetStream, string presetName) => throw new NotSupportedException();

    public Task<PresetImportResult> ImportAssettoCorsaPresetFileAsync(string presetFilePath) => throw new NotSupportedException();

    private Task Store(DriverFormModel model)
    {
        Saved.Add(model);
        Drivers.Add(new DriverModel
        {
            DriverId = model.DriverId,
            DriverName = model.DriverName,
            Car = model.Car,
            CarDisplayName = model.CarDisplayName,
            Skin = model.Skin,
            Nationality = model.Nationality,
            TeamId = model.TeamId,
            TeamName = model.TeamName,
            IsActive = model.IsActive,
            IsHuman = model.IsHuman
        });

        return Task.CompletedTask;
    }
}
