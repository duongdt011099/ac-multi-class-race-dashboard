using Microsoft.EntityFrameworkCore;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;

namespace MulticlassRace.Repositories;

public class QuickRaceRepository : IQuickRaceRepository
{
    private readonly AppDbContext _context;

    public QuickRaceRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<QuickRaceSetup?> GetSetupAsync()
    {
        return _context.QuickRaceSetups.AsNoTracking().FirstOrDefaultAsync();
    }

    public async Task SaveSetupJsonAsync(string setupJson)
    {
        var setup = await _context.QuickRaceSetups.FirstOrDefaultAsync();

        if (setup is null)
        {
            setup = new QuickRaceSetup { QuickRaceSetupId = Guid.NewGuid() };
            _context.QuickRaceSetups.Add(setup);
        }

        setup.SetupJson = setupJson;
        setup.UpdatedUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task SaveLaunchJsonAsync(string launchJson)
    {
        var setup = await _context.QuickRaceSetups.FirstOrDefaultAsync();

        if (setup is null)
        {
            setup = new QuickRaceSetup { QuickRaceSetupId = Guid.NewGuid() };
            _context.QuickRaceSetups.Add(setup);
        }

        setup.LastLaunchJson = launchJson;
        setup.UpdatedUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public Task<List<QuickRaceResult>> GetResultsAsync()
    {
        return _context.QuickRaceResults
            .AsNoTracking()
            .OrderByDescending(r => r.RaceDate)
            .ToListAsync();
    }

    public Task<QuickRaceResult?> GetResultAsync(Guid resultId)
    {
        return _context.QuickRaceResults.AsNoTracking().FirstOrDefaultAsync(r => r.QuickRaceResultId == resultId);
    }

    public async Task AddResultAsync(QuickRaceResult result)
    {
        _context.QuickRaceResults.Add(result);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteResultAsync(Guid resultId)
    {
        var result = await _context.QuickRaceResults.FirstOrDefaultAsync(r => r.QuickRaceResultId == resultId);

        if (result is null)
        {
            return;
        }

        _context.QuickRaceResults.Remove(result);
        await _context.SaveChangesAsync();
    }
}
