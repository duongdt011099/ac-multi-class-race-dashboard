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

    public async Task<List<QuickRacePreset>> GetPresetsAsync()
    {
        var presets = await _context.QuickRacePresets.AsNoTracking().ToListAsync();

        return presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<QuickRacePreset?> GetPresetAsync(Guid presetId)
    {
        return _context.QuickRacePresets.AsNoTracking().FirstOrDefaultAsync(p => p.QuickRacePresetId == presetId);
    }

    public async Task SavePresetAsync(QuickRacePreset preset)
    {
        var existing = await _context.QuickRacePresets.FirstOrDefaultAsync(p => p.QuickRacePresetId == preset.QuickRacePresetId);

        if (existing is null)
        {
            _context.QuickRacePresets.Add(preset);
        }
        else
        {
            existing.Name = preset.Name;
            existing.GridJson = preset.GridJson;
            existing.UpdatedUtc = preset.UpdatedUtc;
        }

        await _context.SaveChangesAsync();
    }

    public async Task DeletePresetAsync(Guid presetId)
    {
        var preset = await _context.QuickRacePresets.FirstOrDefaultAsync(p => p.QuickRacePresetId == presetId);

        if (preset is null)
        {
            return;
        }

        _context.QuickRacePresets.Remove(preset);
        await _context.SaveChangesAsync();
    }
}
