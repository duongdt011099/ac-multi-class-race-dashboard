using MulticlassRace.Builders;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

/// <summary>Writes a race's entered cars into Content Manager's library of grid presets.</summary>
public class GridPresetService : IGridPresetService
{
    private readonly IRaceRepository _raceRepository;
    private readonly IAssettoCorsaGameConfigRepository _configRepository;

    public GridPresetService(IRaceRepository raceRepository, IAssettoCorsaGameConfigRepository configRepository)
    {
        _raceRepository = raceRepository;
        _configRepository = configRepository;
    }

    private sealed record GridRow(Team Team, Driver Driver);

    public async Task<PresetExportResult> ExportGridPresetAsync(
        Guid raceId,
        SessionType sessionType,
        bool humanClassOnly,
        string seasonName,
        string championshipName)
    {
        var race = await LoadRaceAsync(raceId);
        var presetPath = await ResolveExportFolderAsync();
        var rows = await LoadGridRowsAsync(raceId);

        if (humanClassOnly)
        {
            var humanClassItemIds = rows
                .Where(r => r.Driver.IsHuman)
                .Select(r => RaceClassOrdering.ClassItemOf(race, r.Team)?.ClassSettingItemId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            if (humanClassItemIds.Count == 0)
            {
                throw new InvalidOperationException("No human drivers found in the entered teams.");
            }

            rows = rows
                .Where(r => RaceClassOrdering.ClassItemOf(race, r.Team) is { } item && humanClassItemIds.Contains(item.ClassSettingItemId))
                .ToList();
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("No cars available to export from the entered teams.");
        }

        var classOrderByItemId = RaceClassOrdering.OrderById(race);

        var ordered = rows
            .OrderBy(r => RaceClassOrdering.IndexOf(RaceClassOrdering.ClassItemOf(race, r.Team), classOrderByItemId))
            .ThenBy(r => r.Team.TeamName)
            .ThenBy(r => r.Driver.DriverName)
            .ToList();

        var builder = new PresetBuilder();

        foreach (var row in ordered)
        {
            // The player drives their own car in practice and qualifying, so it is not part of the AI grid.
            if (row.Driver.IsHuman && (sessionType == SessionType.Practice || sessionType == SessionType.Qualifying))
            {
                continue;
            }

            AddCar(builder, row.Driver);
        }

        var label = RaceFormat.SessionTypeLabel(sessionType);
        return await WriteAsync(presetPath, $"{label}-{race.RaceName}-{seasonName}-{championshipName}", builder);
    }

    public async Task<PresetExportResult> ExportRaceGridPresetAsync(Guid raceId, string seasonName, string championshipName)
    {
        var race = await LoadRaceAsync(raceId);
        var presetPath = await ResolveExportFolderAsync();
        var rows = await LoadGridRowsAsync(raceId);

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("No cars available to export from the entered teams.");
        }

        var qualifyingSession = (await _raceRepository.GetSessionsByRaceAsync(raceId))
            .Where(s => s.SessionType == SessionType.Qualifying)
            .OrderByDescending(s => s.SessionDate)
            .FirstOrDefault();

        var qualifyingPositionByDriverId = qualifyingSession is null
            ? new Dictionary<Guid, int>()
            : qualifyingSession.DriverStandings
                .Where(d => d.Driver != null)
                .GroupBy(d => d.Driver!.DriverId)
                .ToDictionary(g => g.Key, g => g.Min(d => d.ClassPosition));

        var classOrderByItemId = RaceClassOrdering.OrderById(race);

        var ordered = rows
            .OrderBy(r => RaceClassOrdering.IndexOf(RaceClassOrdering.ClassItemOf(race, r.Team), classOrderByItemId))
            .ThenBy(r => qualifyingPositionByDriverId.TryGetValue(r.Driver.DriverId, out var pos) ? pos : int.MaxValue)
            .ThenBy(r => r.Team.TeamName)
            .ThenBy(r => r.Driver.DriverName)
            .ToList();

        var builder = new PresetBuilder();

        foreach (var row in ordered)
        {
            if (row.Driver.IsHuman)
            {
                // The player's own car is not in the grid, only where they start from.
                builder.SetStartingPosition(qualifyingPositionByDriverId.TryGetValue(row.Driver.DriverId, out var pos) ? pos : 1);
                continue;
            }

            AddCar(builder, row.Driver);
        }

        return await WriteAsync(presetPath, $"Race-{race.RaceName}-{seasonName}-{championshipName}", builder);
    }

    private async Task<Race> LoadRaceAsync(Guid raceId)
    {
        return await _raceRepository.GetRaceWithClassSettingAsync(raceId)
               ?? throw new InvalidOperationException("Race not found.");
    }

    /// <summary>
    /// The preset folder to write to. Exporting creates the folder it writes to, so refuse rather than
    /// grow an empty AcTools tree on a machine that has no Content Manager.
    /// </summary>
    private async Task<string> ResolveExportFolderAsync()
    {
        var config = await _configRepository.GetAsync();
        var presetPath = ContentManagerPaths.ResolvePreset(config?.PresetPath);

        if (ContentManagerPaths.CanExportPresets(config?.PresetPath) is false)
        {
            throw new InvalidOperationException($"Content Manager is not installed, so grid presets have nowhere to be saved. Expected them in: {presetPath}");
        }

        return presetPath;
    }

    private async Task<List<GridRow>> LoadGridRowsAsync(Guid raceId)
    {
        var teams = await _raceRepository.GetEnteredTeamsWithDriversAsync(raceId);

        return teams
            .SelectMany(t => t.Drivers.Where(d => d.IsActive).Select(d => new GridRow(t, d)))
            .ToList();
    }

    private static void AddCar(PresetBuilder builder, Driver driver)
    {
        builder.AddCar(driver.Car, driver.Skin, driver.DriverName, driver.DriverStrength, driver.DriverAgression);
    }

    private static async Task<PresetExportResult> WriteAsync(string presetPath, string baseName, PresetBuilder builder)
    {
        var filePath = Path.Combine(presetPath, $"{ResultFileNames.SanitizeFileName(baseName)}.cmpreset");

        Directory.CreateDirectory(presetPath);

        await File.WriteAllTextAsync(filePath, builder.BuildJson());

        return new PresetExportResult
        {
            FilePath = filePath,
            CarCount = builder.Count
        };
    }
}
