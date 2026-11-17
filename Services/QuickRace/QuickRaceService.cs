using System.Text.Json;
using MulticlassRace.Builders;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Races;
using MulticlassRace.Services.Races.ImportModels;
using MulticlassRace.Services.Results;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.QuickRace;

public class QuickRaceService : IQuickRaceService
{
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

    private readonly IQuickRaceRepository _repository;
    private readonly IImportedLuaResultRepository _importedRepository;
    private readonly RaceSessionChangeNotifier _notifier;

    public QuickRaceService(
        IQuickRaceRepository repository,
        IImportedLuaResultRepository importedRepository,
        RaceSessionChangeNotifier notifier)
    {
        _repository = repository;
        _importedRepository = importedRepository;
        _notifier = notifier;
    }

    public async Task<QuickRaceSetupModel> GetSetupAsync()
    {
        var stored = await _repository.GetSetupAsync();

        return QuickRaceJson.TryDeserialize<QuickRaceSetupModel>(stored?.SetupJson) ?? new QuickRaceSetupModel();
    }

    public Task SaveSetupAsync(QuickRaceSetupModel setup)
    {
        return _repository.SaveSetupJsonAsync(QuickRaceJson.Serialize(setup));
    }

    public async Task<IReadOnlyList<QuickRaceHistoryItem>> GetHistoryAsync()
    {
        var results = await _repository.GetResultsAsync();

        return results
            .Select(r => new QuickRaceHistoryItem(
                r.QuickRaceResultId,
                r.RaceDate,
                r.TrackName,
                r.TrackLayout,
                r.DriverCount,
                r.HumanPosition,
                r.HumanClassPosition,
                r.HumanClassName))
            .ToList();
    }

    public async Task<QuickRaceResultModel?> GetResultAsync(Guid resultId)
    {
        var result = await _repository.GetResultAsync(resultId);

        return result is null ? null : ToModel(result);
    }

    public Task DeleteResultAsync(Guid resultId) => _repository.DeleteResultAsync(resultId);

    public async Task<QuickRaceResultModel> ImportResultFromPathAsync(string fullPath, string? fileName)
    {
        var resolvedName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(fullPath) : fileName.Trim();

        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Result file not found: {resolvedName}");
        }

        var sessionFile = await ReadSessionFileAsync(fullPath, resolvedName);
        var raceSession = sessionFile?.Sessions.FirstOrDefault(s => s is { Type: 3 })
            ?? throw new InvalidOperationException("The selected file contains no race session to import.");

        var existing = await _repository.GetResultsAsync();

        if (existing.Any(r => string.Equals(r.FileName, resolvedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("This race result has already been imported.");
        }

        var storedSetup = await _repository.GetSetupAsync();
        var launch = QuickRaceJson.TryDeserialize<QuickRaceLaunchSnapshot>(storedSetup?.LastLaunchJson)
            ?? new QuickRaceLaunchSnapshot();

        if (launch.Cars.Count == 0 && sessionFile!.Players.Length > 0)
        {
            // No record of the launched grid (a file from elsewhere): read it against the grid as saved now.
            // The first car of a result file is always the player.
            var player = sessionFile.Players[0];
            var saved = QuickRaceJson.TryDeserialize<QuickRaceSetupModel>(storedSetup?.SetupJson) ?? new QuickRaceSetupModel();

            launch.Cars = QuickRaceGrid.BuildLaunchCars(saved, new QuickRaceHuman(player.Name, player.Car, player.Skin, player.Car)).Cars.ToList();
        }

        var rows = BuildRows(sessionFile!, raceSession, launch);

        // A race that followed a qualifying session keeps where each car qualified.
        var raceSetup = QuickRaceJson.TryDeserialize<QuickRaceSetupModel>(storedSetup?.SetupJson);
        AttachQualifying(rows, raceSetup?.QualifyingRows);

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("The selected file has no drivers to import.");
        }

        var human = rows.FirstOrDefault(r => r.IsHuman);

        var entity = new QuickRaceResult
        {
            QuickRaceResultId = Guid.NewGuid(),
            RaceDate = LuaResultPaths.TryParseFileName(resolvedName, out var date, out _) ? date : DateTime.Now,
            FileName = resolvedName,
            TrackName = string.IsNullOrWhiteSpace(sessionFile!.Track) ? launch.TrackName : sessionFile.Track,
            TrackLayout = launch.TrackLayout,
            DriverCount = rows.Count,
            HumanPosition = human?.Position,
            HumanClassPosition = human?.ClassPosition,
            HumanClassName = human is null || string.IsNullOrWhiteSpace(human.ClassName) ? null : human.ClassName,
            ResultJson = QuickRaceJson.Serialize(rows)
        };

        await _repository.AddResultAsync(entity);
        await RecordImportedAsync(fullPath, resolvedName, entity.RaceDate);

        // The race is over: the next one needs its own qualifying.
        if (raceSetup is { QualifyingCompleted: true })
        {
            QuickRaceGrid.ClearQualifying(raceSetup);
            await _repository.SaveSetupJsonAsync(QuickRaceJson.Serialize(raceSetup));
        }

        var model = ToModel(entity);
        _notifier.NotifyQuickRaceImported(model.ResultId);

        return model;
    }

    public async Task<QuickRaceQualifyingModel> ImportQualifyingFromPathAsync(string fullPath, string? fileName)
    {
        var resolvedName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(fullPath) : fileName.Trim();

        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Result file not found: {resolvedName}");
        }

        List<SessionResultEntry>? entries;

        try
        {
            await using var stream = File.OpenRead(fullPath);
            entries = await JsonSerializer.DeserializeAsync<List<SessionResultEntry>>(stream, CaseInsensitiveJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The selected file is not a valid qualifying result file: {resolvedName}", ex);
        }

        if (entries is null || entries.Count == 0)
        {
            throw new InvalidOperationException("The qualifying result has no lap times.");
        }

        var storedSetup = await _repository.GetSetupAsync();
        var launch = QuickRaceJson.TryDeserialize<QuickRaceLaunchSnapshot>(storedSetup?.LastLaunchJson);
        var setup = QuickRaceJson.TryDeserialize<QuickRaceSetupModel>(storedSetup?.SetupJson);

        if (launch is null || launch.Cars.Count == 0 || setup is null)
        {
            throw new InvalidOperationException("Start the qualifying from the Quick Race page so the grid is known.");
        }

        var rows = BuildQualifyingRows(entries, launch);

        QuickRaceGrid.ApplyQualifying(setup, rows);
        await _repository.SaveSetupJsonAsync(QuickRaceJson.Serialize(setup));

        var date = LuaResultPaths.TryParseFileName(resolvedName, out var parsed, out _) ? parsed : DateTime.Now;
        await RecordImportedAsync(fullPath, resolvedName, date, SessionType.Qualifying);

        var model = new QuickRaceQualifyingModel { Date = date, Rows = rows };
        _notifier.NotifyQuickQualifyingImported(model);

        return model;
    }

    /// <summary>Every launched car in qualifying order: timed cars by best lap, the rest after them in grid order.</summary>
    internal static List<QuickRaceResultRow> BuildQualifyingRows(
        IReadOnlyList<SessionResultEntry> entries,
        QuickRaceLaunchSnapshot launch)
    {
        var used = new HashSet<SessionResultEntry>();
        var items = new List<(QuickRaceResultRow Row, int Index)>();

        foreach (var car in launch.Cars.OrderBy(c => c.Index))
        {
            var entry = entries.FirstOrDefault(e => !used.Contains(e)
                && SameCar(e.Car, car.Car)
                && string.Equals(e.Driver?.Trim(), car.DriverName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (entry is not null)
            {
                used.Add(entry);
            }

            items.Add((new QuickRaceResultRow
            {
                DriverName = car.DriverName,
                Car = car.Car,
                Skin = string.IsNullOrWhiteSpace(entry?.Skin) ? car.Skin : entry!.Skin,
                CarDisplayName = string.IsNullOrWhiteSpace(car.CarDisplayName) ? car.Car : car.CarDisplayName,
                TeamName = car.TeamName,
                ClassName = car.ClassName,
                ClassColor = car.ClassColor,
                BestLapTimeMs = TryParseLapTime(entry?.BestLapTimeMs),
                IsHuman = car.IsHuman
            }, car.Index));
        }

        var ordered = items
            .OrderBy(i => i.Row.BestLapTimeMs > 0 ? 0 : 1)
            .ThenBy(i => i.Row.BestLapTimeMs)
            .ThenBy(i => i.Index)
            .Select(i => i.Row)
            .ToList();

        var classCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Position = i + 1;
            classCounts.TryGetValue(ordered[i].ClassName, out var inClass);
            classCounts[ordered[i].ClassName] = ++inClass;
            ordered[i].ClassPosition = inClass;
        }

        return ordered;
    }

    private static int TryParseLapTime(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && TimeSpan.TryParseExact(value.Trim(), new[] { @"mm\:ss\.fff", @"m\:ss\.fff" }, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return (int)Math.Round(parsed.TotalMilliseconds);
        }

        return 0;
    }

    /// <summary>
    /// Ranks every car of the result file: overall by finishing order (then laps, then best lap), and within
    /// its class by that same order. The safety car is never a competitor.
    /// </summary>
    internal static List<QuickRaceResultRow> BuildRows(
        CmSessionFile file,
        CmSession raceSession,
        QuickRaceLaunchSnapshot launch)
    {
        var entries = new List<(QuickRaceResultRow Row, int FinishOrder)>();
        var usedIndexes = new HashSet<int>();

        for (var carIndex = 0; carIndex < file.Players.Length; carIndex++)
        {
            var player = file.Players[carIndex];

            if (string.Equals(player.Name?.Trim(), RaceIniBuilder.SafetyCarDriverName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The grid index is the car's slot in race.ini, which is also its place in the result file.
            // A different car in that slot means the file belongs to another grid, so fall back to names.
            var launched = launch.Cars.FirstOrDefault(c => c.Index == carIndex && SameCar(c.Car, player.Car))
                ?? launch.Cars.FirstOrDefault(c =>
                    !usedIndexes.Contains(c.Index)
                    && SameCar(c.Car, player.Car)
                    && string.Equals(c.DriverName.Trim(), player.Name?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (launched is not null)
            {
                usedIndexes.Add(launched.Index);
            }

            var bestLapMs = (int)(raceSession.BestLaps?.FirstOrDefault(b => b.Car == carIndex && b.Time > 0)?.Time ?? 0);

            var lapCount = raceSession.LapsTotal is { Count: > 0 } && carIndex < raceSession.LapsTotal.Count
                ? raceSession.LapsTotal[carIndex]
                : raceSession.LapsCount;

            var finishOrder = raceSession.RaceResult?.IndexOf(carIndex) ?? -1;

            entries.Add((new QuickRaceResultRow
            {
                DriverName = launched?.DriverName ?? player.Name?.Trim() ?? string.Empty,
                Car = player.Car,
                Skin = player.Skin,
                CarDisplayName = string.IsNullOrWhiteSpace(launched?.CarDisplayName) ? player.Car : launched!.CarDisplayName,
                TeamName = launched?.TeamName,
                ClassName = launched?.ClassName ?? string.Empty,
                ClassColor = launched?.ClassColor ?? RaceFormat.UnknownClassColor,
                LapCount = lapCount,
                BestLapTimeMs = bestLapMs,
                IsHuman = launched?.IsHuman ?? false
            }, finishOrder < 0 ? int.MaxValue : finishOrder));
        }

        var ordered = entries
            .OrderBy(e => e.FinishOrder)
            .ThenByDescending(e => e.Row.LapCount)
            .ThenBy(e => e.Row.BestLapTimeMs > 0 ? e.Row.BestLapTimeMs : int.MaxValue)
            .Select(e => e.Row)
            .ToList();

        var classCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < ordered.Count; i++)
        {
            var row = ordered[i];
            row.Position = i + 1;

            classCounts.TryGetValue(row.ClassName, out var inClass);
            classCounts[row.ClassName] = ++inClass;
            row.ClassPosition = inClass;
        }

        return ordered;
    }

    internal static void AttachQualifying(IReadOnlyList<QuickRaceResultRow> rows, IReadOnlyList<QuickRaceResultRow>? qualifying)
    {
        if (qualifying is null || qualifying.Count == 0)
        {
            return;
        }

        var used = new HashSet<QuickRaceResultRow>();

        foreach (var row in rows)
        {
            var match = row.IsHuman
                ? qualifying.FirstOrDefault(q => q.IsHuman)
                : qualifying.FirstOrDefault(q => !q.IsHuman && !used.Contains(q)
                    && string.Equals(q.DriverName.Trim(), row.DriverName.Trim(), StringComparison.OrdinalIgnoreCase)
                    && SameCar(q.Car, row.Car));

            if (match is null)
            {
                continue;
            }

            used.Add(match);
            row.QualifyingPosition = match.Position;
            row.QualifyingTimeMs = match.BestLapTimeMs > 0 ? match.BestLapTimeMs : null;
        }
    }

    private static bool SameCar(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private async Task RecordImportedAsync(string fullPath, string fileName, DateTime date, SessionType sessionType = SessionType.Race)
    {
        try
        {
            var existing = await _importedRepository.FindByFullPathAsync(fullPath);

            if (existing is not null)
            {
                existing.Imported = true;
                existing.ImportCount += 1;
                existing.ImportedAtUtc = DateTime.UtcNow;
                existing.LastError = null;
                await _importedRepository.UpdateAsync(existing);
                return;
            }

            await _importedRepository.AddAsync(new ImportedLuaResult
            {
                ImportedLuaResultId = Guid.NewGuid(),
                FileName = fileName,
                FullPath = fullPath,
                SessionType = sessionType,
                SessionDate = date,
                Imported = true,
                ImportCount = 1,
                ImportedAtUtc = DateTime.UtcNow
            });
        }
        catch
        {
            // Bookkeeping only: the result is already saved, and a duplicate offer is harmless.
        }
    }

    private static async Task<CmSessionFile?> ReadSessionFileAsync(string fullPath, string displayName)
    {
        try
        {
            await using var stream = File.OpenRead(fullPath);

            return await JsonSerializer.DeserializeAsync<CmSessionFile>(stream, CaseInsensitiveJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The selected file is not a valid race result file: {displayName}", ex);
        }
    }

    private static QuickRaceResultModel ToModel(QuickRaceResult result)
    {
        return new QuickRaceResultModel
        {
            ResultId = result.QuickRaceResultId,
            RaceDate = result.RaceDate,
            FileName = result.FileName,
            TrackName = result.TrackName,
            TrackLayout = result.TrackLayout,
            DriverCount = result.DriverCount,
            HumanPosition = result.HumanPosition,
            HumanClassPosition = result.HumanClassPosition,
            HumanClassName = result.HumanClassName,
            Rows = QuickRaceJson.TryDeserialize<List<QuickRaceResultRow>>(result.ResultJson) ?? new List<QuickRaceResultRow>()
        };
    }
}
