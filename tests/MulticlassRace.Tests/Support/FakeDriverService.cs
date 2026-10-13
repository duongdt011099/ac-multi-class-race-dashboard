using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.Support;

/// <summary>In-memory driver list, so page tests do not depend on the machine's Assetto Corsa install.</summary>
public sealed class FakeDriverService : IDriverService
{
    public List<DriverModel> Drivers { get; } = new();

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

    public Task CreateDriverAsync(DriverFormModel model) => throw new NotSupportedException();

    public Task UpdateDriverAsync(DriverFormModel model) => throw new NotSupportedException();

    public Task DeleteDriverAsync(Guid driverId) => throw new NotSupportedException();

    public Task<PresetImportResult> ImportAssettoCorsaPresetAsync(Stream presetStream, string presetName) => throw new NotSupportedException();

    public Task<PresetImportResult> ImportAssettoCorsaPresetFileAsync(string presetFilePath) => throw new NotSupportedException();
}
