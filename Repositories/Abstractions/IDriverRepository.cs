using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface IDriverRepository : IGenericRepository<Driver>
{
    Task<IEnumerable<Driver>> GetActiveDriversAsync();
    Task<IEnumerable<Driver>> GetDriversByTeamAsync(Guid teamId);
    Task<Driver?> GetDriverWithDetailsAsync(Guid driverId);
    Task DeacivateDriverAsync(Guid driverId);
    Task<IEnumerable<Driver>> SearchUnassignedDriversAsync(string? searchTerm, int limit = 10);
    Task<int> AddMissingAsync(IEnumerable<Driver> candidates);
    Task UnsetHumanFlagAsync(Guid? exceptDriverId = null);
}
