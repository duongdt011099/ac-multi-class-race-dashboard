using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class TeamClassRepository : GenericRepository<TeamClass>, ITeamClassRepository
{
    private readonly ILogger<TeamClassRepository> _logger;

    public TeamClassRepository(AppDbContext context, ILogger<TeamClassRepository> logger) : base(context)
    {
        _logger = logger;
    }

    public async Task DeactivateTeamClassAsync(Guid teamClassId)
    {
        var teamClass = await _context.TeamClasses.FirstOrDefaultAsync(tc => tc.TeamClassId == teamClassId);

        if (teamClass is not null)
        {
            teamClass.IsActive = false;
            await _context.SaveChangesAsync();

            return;
        }

        _logger.LogWarning("Team class with ID {TeamClassId} not found for deactivation.", teamClassId);
    }

    public async Task<IEnumerable<TeamClass>> GetActiveTeamClassesAsync()
    {
        return await _context.TeamClasses
            .Where(tc => tc.IsActive)
            .OrderBy(tc => tc.TeamClassName)
            .ToListAsync();
    }
}
