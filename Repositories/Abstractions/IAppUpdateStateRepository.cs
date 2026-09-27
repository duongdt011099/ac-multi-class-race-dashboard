using MulticlassRace.Models;

namespace MulticlassRace.Repositories.Abstractions;

public interface IAppUpdateStateRepository : IGenericRepository<AppUpdateState>
{
    Task<AppUpdateState?> GetAsync();
}
