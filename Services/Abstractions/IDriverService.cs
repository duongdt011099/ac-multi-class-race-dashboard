using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Abstractions;

public interface IDriverService
{
    Task<IEnumerable<DriverModel>> SearchAvailableDriversAsync(string? searchTerm);
    Task<IEnumerable<DriverModel>> GetActiveDriversAsync();
    Task<DriverFormModel?> GetDriverByIdAsync(Guid driverId);
    Task CreateDriverAsync(DriverFormModel model);
    Task UpdateDriverAsync(DriverFormModel model);
    Task DeleteDriverAsync(Guid driverId);
    Task<PresetImportResult> ImportAssettoCorsaPresetAsync(Stream presetStream, string presetName);
}