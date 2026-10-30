using System.Globalization;
using System.Text.Json;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Races.ImportModels;
using MulticlassRace.Services.Results;

namespace MulticlassRace.Services.Races;

/// <summary>
/// Finds session result files and turns them into recorded sessions. Drivers in a result are matched
/// to the race's entered drivers by car, skin and name; anyone who does not match is skipped.
/// </summary>
public class RaceResultService : IRaceResultService
{
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IRaceRepository _raceRepository;
    private readonly IAssettoCorsaGameConfigRepository _configRepository;
    private readonly IPointSettingRepository _pointSettingRepository;

    public RaceResultService(
        IRaceRepository raceRepository,
        IAssettoCorsaGameConfigRepository configRepository,
        IPointSettingRepository pointSettingRepository)
    {
        _raceRepository = raceRepository;
        _configRepository = configRepository;
        _pointSettingRepository = pointSettingRepository;
    }

    public async Task<IEnumerable<RaceResultFileModel>> GetRaceResultFilesAsync()
    {
        var config = await _configRepository.GetAsync();
        var contentManagerFolder = ContentManagerPaths.ResolveResults(config?.RaceResultsPath);

        // Lua-produced race results live in their own subfolder so a declined or failed
        // auto-import can still be retried through the same manual dropdown.
        var luaRaceFolder = Path.Combine(LuaResultPaths.ResolveRoot(config?.LuaResultPath), LuaResultPaths.RaceFolder);

        return ReadResultFiles(contentManagerFolder, isLuaResult: false)
            .Concat(ReadResultFiles(luaRaceFolder, isLuaResult: true))
            .OrderByDescending(f => f.SessionDate)
            .ToList();
    }

    public async Task<ResultImportAvailabilityModel> GetResultImportAvailabilityAsync()
    {
        var config = await _configRepository.GetAsync();

        var resultsFolder = ContentManagerPaths.ResolveResults(config?.RaceResultsPath);

        // The dropdown offers Content Manager results and the Lua app's own race folder, so either one
        // being there is enough to show it.
        var luaRaceFolder = Path.Combine(LuaResultPaths.ResolveRoot(config?.LuaResultPath), LuaResultPaths.RaceFolder);

        return new ResultImportAvailabilityModel
        {
            CanImportResults = Directory.Exists(resultsFolder) || Directory.Exists(luaRaceFolder),
            CanExportPresets = ContentManagerPaths.CanExportPresets(config?.PresetPath)
        };
    }

    public async Task<RaceResultImportResult> ImportRaceResultAsync(Guid raceId, string fileName)
    {
        var config = await _configRepository.GetAsync();
        var path = ContentManagerPaths.ResolveResults(config?.RaceResultsPath);

        if (Directory.Exists(path) is false)
        {
            throw new InvalidOperationException($"There are no race results to import in {path}");
        }

        var filePath = Path.Combine(path, fileName);

        return await ImportRaceResultFromPathAsync(raceId, filePath, fileName);
    }

    public async Task<RaceResultImportResult> ImportRaceResultFromPathAsync(Guid raceId, string fullPath, string? fileName)
    {
        var resolvedName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(fullPath) : fileName.Trim();

        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Result file not found: {resolvedName}");
        }

        var sessionFile = await ReadSessionFileAsync(fullPath, resolvedName);

        if (sessionFile is null || sessionFile.Sessions.Length == 0)
        {
            throw new InvalidOperationException("The selected file contains no sessions.");
        }

        var raceSession = sessionFile.Sessions.FirstOrDefault(s => s is { Type: 3 });

        if (raceSession is null)
        {
            throw new InvalidOperationException("The selected file contains no race session to import.");
        }

        var race = await LoadRaceAsync(raceId);
        var pointSetting = await _pointSettingRepository.GetByIdAsync(race.PointSettingId);
        var teams = (await _raceRepository.GetEnteredTeamsWithDriversAsync(raceId)).ToList();
        var driverByKey = BuildDriverLookup(teams);

        var entries = new List<ImportEntry>();

        for (var carIndex = 0; carIndex < sessionFile.Players.Length; carIndex++)
        {
            var player = sessionFile.Players[carIndex];

            if (!driverByKey.TryGetValue(BuildDriverKey(player.Car, player.Skin, player.Name), out var match))
            {
                continue;
            }

            var bestLapMs = (int)(raceSession.BestLaps?
                .FirstOrDefault(b => b.Car == carIndex && b.Time > 0)?.Time ?? 0);

            var lapCount = raceSession.LapsTotal is { Count: > 0 } && carIndex < raceSession.LapsTotal.Count
                ? raceSession.LapsTotal[carIndex]
                : raceSession.LapsCount;

            var finishOrder = raceSession.RaceResult?.IndexOf(carIndex) ?? -1;

            if (finishOrder < 0)
            {
                finishOrder = int.MaxValue;
            }

            entries.Add(new ImportEntry
            {
                Match = match,
                BestLapMs = bestLapMs,
                LapCount = lapCount,
                FinishOrder = finishOrder
            });
        }

        var session = NewSession(race, SessionType.Race, resolvedName);

        var ranked = RankWithinClasses(
            race,
            entries,
            classEntries => classEntries
                .OrderBy(e => e.FinishOrder)
                .ThenByDescending(e => e.LapCount)
                .ThenBy(e => e.BestLapMs));

        foreach (var item in ranked)
        {
            var points = pointSetting?.Config.TryGetValue(item.ClassPosition.ToString(), out var awarded) == true ? awarded : 0;

            session.DriverStandings.Add(CreateStanding(race, item, points, item.Entry.LapCount));
        }

        await _raceRepository.SaveRaceSessionAsync(raceId, session);

        race.Status = RaceStatus.Finished;
        await _raceRepository.UpdateAsync(race);

        return new RaceResultImportResult
        {
            Imported = session.DriverStandings.Count,
            Skipped = sessionFile.Players.Length - session.DriverStandings.Count,
            SessionDate = session.SessionDate,
            SessionType = session.SessionType
        };
    }

    public async Task<RaceResultImportResult> ImportSessionResultAsync(Guid raceId, SessionType sessionType, Stream stream, string? fileName)
    {
        if (sessionType is not (SessionType.Practice or SessionType.Qualifying))
        {
            throw new InvalidOperationException("Only practice and qualifying results can be imported this way.");
        }

        List<SessionResultEntry>? entries;

        try
        {
            entries = await JsonSerializer.DeserializeAsync<List<SessionResultEntry>>(stream, CaseInsensitiveJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The selected file is not a valid session result file.", ex);
        }

        if (entries is null || entries.Count == 0)
        {
            throw new InvalidOperationException("The selected file contains no result entries.");
        }

        var race = await LoadRaceAsync(raceId);
        var teams = (await _raceRepository.GetEnteredTeamsWithDriversAsync(raceId)).ToList();
        var driverByKey = BuildDriverLookup(teams);

        var timed = new List<ImportEntry>();
        var skipped = 0;

        foreach (var entry in entries)
        {
            if (!driverByKey.TryGetValue(BuildDriverKey(entry.Car, entry.Skin, entry.Driver), out var match))
            {
                skipped++;
                continue;
            }

            if (!TryParseLapTime(entry.BestLapTimeMs, out var lapMs))
            {
                continue;
            }

            timed.Add(new ImportEntry
            {
                Match = match,
                BestLapMs = lapMs,
                LapCount = 0
            });
        }

        // Entered drivers missing from the file (or without a usable time) still get a row, after the timed ones.
        var timedKeys = timed
            .Select(e => BuildDriverKey(e.Match.Driver.Car, e.Match.Driver.Skin, e.Match.Driver.DriverName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var untimed = teams
            .SelectMany(team => team.Drivers
                .Where(d => d.IsActive)
                .Where(d => !timedKeys.Contains(BuildDriverKey(d.Car, d.Skin, d.DriverName)))
                .Select(d => new ImportEntry { Match = (d, team), BestLapMs = 0, LapCount = 0 }))
            .OrderBy(e => e.Match.Team.TeamName)
            .ThenBy(e => e.Match.Driver.DriverName)
            .ToList();

        var session = NewSession(race, sessionType, fileName ?? string.Empty);

        var ranked = RankWithinClasses(
            race,
            timed.Concat(untimed),
            classEntries => classEntries.OrderByDescending(e => e.BestLapMs > 0).ThenBy(e => e.BestLapMs));

        foreach (var item in ranked)
        {
            session.DriverStandings.Add(CreateStanding(race, item, points: 0, lapCount: 0));
        }

        await _raceRepository.SaveRaceSessionAsync(raceId, session);

        if (race.Status != RaceStatus.Finished)
        {
            race.Status = RaceStatus.InProgress;
            await _raceRepository.UpdateAsync(race);
        }

        return new RaceResultImportResult
        {
            Imported = session.DriverStandings.Count,
            Skipped = skipped,
            SessionDate = session.SessionDate,
            SessionType = session.SessionType
        };
    }

    private async Task<Race> LoadRaceAsync(Guid raceId)
    {
        return await _raceRepository.GetRaceWithClassSettingAsync(raceId)
               ?? throw new InvalidOperationException("Race not found.");
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
            throw new InvalidOperationException(
                $"The selected file is not a valid Content Manager session file: {displayName}",
                ex);
        }
    }

    private static List<RaceResultFileModel> ReadResultFiles(string folder, bool isLuaResult)
    {
        if (!Directory.Exists(folder))
        {
            return new List<RaceResultFileModel>();
        }

        return Directory
            .GetFiles(folder, "*" + LuaResultPaths.FileExtension)
            .Select(f => new { FileName = Path.GetFileName(f), FilePath = f })
            .Where(f => ResultFileNames.TryParseSessionDate(f.FileName, out _))
            .OrderByDescending(f => f.FileName)
            .Select(f =>
            {
                ResultFileNames.TryParseSessionDate(f.FileName, out var date);

                return new RaceResultFileModel
                {
                    FileName = f.FileName,
                    FullPath = f.FilePath,
                    SessionDate = date,
                    Track = ReadTrackOfRaceSession(f.FilePath) ?? string.Empty,
                    IsLuaResult = isLuaResult
                };
            })
            .Where(f => !string.IsNullOrWhiteSpace(f.Track))
            .ToList();
    }

    /// <summary>The track named in the file, but only when the file actually holds a race session.</summary>
    private static string? ReadTrackOfRaceSession(string filePath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = document.RootElement;

            if (!root.TryGetProperty("track", out var track))
            {
                return null;
            }

            var hasRace = root.TryGetProperty("sessions", out var sessions) &&
                sessions.ValueKind == JsonValueKind.Array &&
                sessions.EnumerateArray().Any(s => s.TryGetProperty("type", out var type) && type.GetInt32() == 3);

            return hasRace ? (track.GetString() ?? string.Empty) : null;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, (Driver Driver, Team Team)> BuildDriverLookup(IEnumerable<Team> teams)
    {
        var driverByKey = new Dictionary<string, (Driver Driver, Team Team)>(StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            foreach (var driver in team.Drivers.Where(d => d.IsActive))
            {
                driverByKey[BuildDriverKey(driver.Car, driver.Skin, driver.DriverName)] = (driver, team);
            }
        }

        return driverByKey;
    }

    private static string BuildDriverKey(string car, string skin, string driverName)
    {
        return car + "\u001F" + skin + "\u001F" + (driverName ?? string.Empty).Trim();
    }

    private static Session NewSession(Race race, SessionType type, string fileName)
    {
        return new Session
        {
            SessionId = Guid.NewGuid(),
            SessionType = type,
            SessionDate = ResultFileNames.TryParseSessionDate(fileName, out var date) ? date : DateTime.Now,
            RaceId = race.RaceId,
            Race = race,
            DriverStandings = new List<DriverStanding>()
        };
    }

    private sealed record RankedEntry(ImportEntry Entry, int ClassPosition, string TeamClassName);

    /// <summary>Groups entries by class (leading class first) and numbers them within each class using the given order.</summary>
    private static List<RankedEntry> RankWithinClasses(
        Race race,
        IEnumerable<ImportEntry> entries,
        Func<IEnumerable<ImportEntry>, IEnumerable<ImportEntry>> orderWithinClass)
    {
        var classOrderByName = RaceClassOrdering.OrderByName(race);

        return entries
            .GroupBy(e => RaceClassOrdering.ClassNameOf(race, e.Match.Team))
            .OrderBy(g => RaceClassOrdering.IndexOfName(g.Key, classOrderByName))
            .SelectMany(g => orderWithinClass(g).Select((e, index) => new RankedEntry(e, index + 1, g.Key)))
            .ToList();
    }

    private static DriverStanding CreateStanding(Race race, RankedEntry item, int points, int lapCount)
    {
        return new DriverStanding
        {
            DriverStandingId = Guid.NewGuid(),
            Driver = item.Entry.Match.Driver,
            DriverName = item.Entry.Match.Driver.DriverName,
            OriginalTeamId = item.Entry.Match.Team.TeamId,
            TeamName = item.Entry.Match.Team.TeamName,
            TeamClassName = item.TeamClassName,
            Race = race,
            Position = item.ClassPosition,
            ClassPosition = item.ClassPosition,
            Points = points,
            BestLapTimeMs = item.Entry.BestLapMs,
            LapCount = lapCount
        };
    }

    private static bool TryParseLapTime(string? value, out int lapTimeMs)
    {
        lapTimeMs = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!TimeSpan.TryParseExact(
            value.Trim(),
            new[] { @"mm\:ss\.fff", @"m\:ss\.fff" },
            CultureInfo.InvariantCulture,
            out var parsed))
        {
            return false;
        }

        lapTimeMs = (int)Math.Round(parsed.TotalMilliseconds);
        return lapTimeMs > 0;
    }
}
