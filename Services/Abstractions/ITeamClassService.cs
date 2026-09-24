using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Abstractions;

public interface ITeamClassService
{
    Task<IEnumerable<TeamClassModel>> GetActiveTeamClassesAsync();
}
