using System.Text.Json;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class RaceService : IRaceService
{
    private readonly IRaceRepository _raceRepository;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IAssettoCorsaGameConfigRepository _configRepository;
    private readonly IPointSettingRepository _pointSettingRepository;

    private static readonly JsonSerializerOptions CaseInsensitiveJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RaceService(
        IRaceRepository raceRepository,
        ISeasonRepository seasonRepository,
        IAssettoCorsaGameConfigRepository configRepository,
        IPointSettingRepository pointSettingRepository)
    {
        _raceRepository = raceRepository;
        _seasonRepository = seasonRepository;
        _configRepository = configRepository;
        _pointSettingRepository = pointSettingRepository;
    }

    public async Task<IEnumerable<RaceModel>> GetRacesBySeasonAsync(Guid seasonId)
    {
        var races = await _raceRepository.GetRacesBySeasonAsync(seasonId);

        return races.Select(r => new RaceModel
        {
            RaceId = r.RaceId,
            RaceName = r.RaceName,
            Country = r.Country,
            Status = r.Status,
            PointSettingId = r.PointSettingId,
            PointSettingName = r.PointSetting.SettingName,
            StandingCount = r.Sessions.Sum(s => s.DriverStandings.Count)
        });
    }

    public async Task CreateRaceAsync(Guid seasonId, string raceName, string country, Guid pointSettingId)
    {
        var season = await _seasonRepository.GetByIdAsync(seasonId);

        if (season is null)
        {
            throw new InvalidOperationException("Season not found.");
        }

        var pointSetting = await _pointSettingRepository.GetByIdAsync(pointSettingId);

        if (pointSetting is null)
        {
            throw new InvalidOperationException("A point setting must be selected.");
        }

        var race = new Race
        {
            RaceId = Guid.NewGuid(),
            RaceName = raceName.Trim(),
            Country = country?.Trim() ?? string.Empty,
            Status = RaceStatus.NotStarted,
            PointSettingId = pointSetting.SettingId,
            PointSetting = pointSetting,
            Season = season,
            Sessions = new List<Session>(),
            EnteredTeams = new List<Team>()
        };

        await _raceRepository.AddAsync(race);
    }

    public async Task UpdateRaceAsync(RaceFormModel model)
    {
        var race = await _raceRepository.GetByIdAsync(model.RaceId);

        if (race is null)
        {
            return;
        }

        race.RaceName = model.RaceName.Trim();
        race.Country = model.Country?.Trim() ?? string.Empty;

        if (model.PointSettingId != Guid.Empty && race.PointSettingId != model.PointSettingId)
        {
            var pointSetting = await _pointSettingRepository.GetByIdAsync(model.PointSettingId);

            if (pointSetting is not null)
            {
                race.PointSettingId = pointSetting.SettingId;
                race.PointSetting = pointSetting;
            }
        }

        await _raceRepository.UpdateAsync(race);
    }

    public async Task DeleteRaceAsync(Guid raceId)
    {
        await _raceRepository.DeleteAsync(raceId);
    }

    public async Task<IEnumerable<SessionModel>> GetRaceSessionsAsync(Guid raceId)
    {
        var sessions = await _raceRepository.GetSessionsByRaceAsync(raceId);

        return sessions.Select(s => new SessionModel
        {
            SessionId = s.SessionId,
            SessionType = s.SessionType,
            SessionDate = s.SessionDate,
            DriverStandings = s.DriverStandings
                .OrderBy(d => d.ClassPosition)
                .Select(d => new DriverStandingModel
                {
                    Position = d.Position,
                    DriverName = d.DriverName,
                    TeamName = d.TeamName,
                    TeamClassName = d.TeamClassName,
                    BestLapTimeMs = d.BestLapTimeMs,
                    Points = d.Points,
                    LapCount = d.LapCount,
                    ClassPosition = d.ClassPosition
                })
                .ToList()
        });
    }

    public async Task<IEnumerable<TeamModel>> GetRaceTeamsAsync(Guid raceId)
    {
        var teams = await _raceRepository.GetEnteredTeamsAsync(raceId);

        return teams.Select(t => new TeamModel
        {
            TeamId = t.TeamId,
            TeamName = t.TeamName,
            TeamLogo = t.TeamLogo,
            TeamClass = t.TeamClass.TeamClassName,
            TeamClassId = t.TeamClass.TeamClassId,
            IsActive = t.IsActive
        });
    }

    public async Task SaveRaceTeamsAsync(Guid raceId, IReadOnlyCollection<Guid> teamIds)
    {
        await _raceRepository.UpdateRaceTeamsAsync(raceId, teamIds);
    }

    public async Task<PresetExportResult> ExportGridPresetAsync(Guid raceId, SessionType sessionType, bool humanClassOnly, string seasonName, string championshipName)
    {
        var race = await _raceRepository.GetByIdAsync(raceId);

        if (race is null)
        {
            throw new InvalidOperationException("Race not found.");
        }

        var config = await _configRepository.GetAsync();
        var presetPath = config?.PresetPath?.Trim();

        if (string.IsNullOrWhiteSpace(presetPath))
        {
            throw new InvalidOperationException("Preset path is not configured. Set it on the Game Config page.");
        }

        var teams = (await _raceRepository.GetEnteredTeamsWithDriversAsync(raceId)).ToList();

        var rows = teams
            .SelectMany(t => t.Drivers
                .Where(d => d.IsActive)
                .Select(d => new { Team = t, Driver = d }))
            .ToList();

        if (humanClassOnly)
        {
            var humanClassIds = rows
                .Where(r => r.Driver.IsHuman)
                .Select(r => r.Team.TeamClass.TeamClassId)
                .ToHashSet();

            if (humanClassIds.Count == 0)
            {
                throw new InvalidOperationException("No human drivers found in the entered teams.");
            }

            rows = rows.Where(r => humanClassIds.Contains(r.Team.TeamClass.TeamClassId)).ToList();
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("No cars available to export from the entered teams.");
        }

        var ordered = rows
            .OrderBy(r => r.Team.TeamClass.TeamClassName)
            .ThenBy(r => r.Team.TeamName)
            .ThenBy(r => r.Driver.DriverName)
            .ToList();

        var builder = new PresetBuilder();

        foreach (var row in ordered)
        {
            builder.AddCar(row.Driver.Car, row.Driver.Skin, row.Driver.DriverName, row.Driver.DriverStrength, row.Driver.DriverAgression);
        }

        var fileName = $"{SanitizeFileName($"{SessionTypeLabel(sessionType)}-{race.RaceName}-{seasonName}-{championshipName}")}.cmpreset";
        var filePath = Path.Combine(presetPath, fileName);

        Directory.CreateDirectory(presetPath);

        await File.WriteAllTextAsync(filePath, builder.BuildJson());

        return new PresetExportResult
        {
            FilePath = filePath,
            CarCount = builder.Count
        };
    }

    public async Task<PresetExportResult> ExportRaceGridPresetAsync(Guid raceId, string seasonName, string championshipName)
    {
        var race = await _raceRepository.GetByIdAsync(raceId);

        if (race is null)
        {
            throw new InvalidOperationException("Race not found.");
        }

        var config = await _configRepository.GetAsync();
        var presetPath = config?.PresetPath?.Trim();

        if (string.IsNullOrWhiteSpace(presetPath))
        {
            throw new InvalidOperationException("Preset path is not configured. Set it on the Game Config page.");
        }

        var teams = (await _raceRepository.GetEnteredTeamsWithDriversAsync(raceId)).ToList();

        var rows = teams
            .SelectMany(t => t.Drivers
                .Where(d => d.IsActive)
                .Select(d => new { Team = t, Driver = d }))
            .ToList();

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

        var ordered = rows
            .OrderBy(r => ClassOrderIndex(r.Team.TeamClass?.TeamClassName))
            .ThenBy(r => qualifyingPositionByDriverId.TryGetValue(r.Driver.DriverId, out var pos) ? pos : int.MaxValue)
            .ThenBy(r => r.Team.TeamName)
            .ThenBy(r => r.Driver.DriverName)
            .ToList();

        var builder = new PresetBuilder();

        foreach (var row in ordered)
        {
            if (row.Driver.IsHuman)
            {
                builder.SetStartingPosition(qualifyingPositionByDriverId.TryGetValue(row.Driver.DriverId, out var pos) ? pos : 1);
            }
            
            builder.AddCar(row.Driver.Car, row.Driver.Skin, row.Driver.DriverName, row.Driver.DriverStrength, row.Driver.DriverAgression);
        }

        var fileName = $"{SanitizeFileName($"Race-{race.RaceName}-{seasonName}-{championshipName}")}.cmpreset";
        var filePath = Path.Combine(presetPath, fileName);

        Directory.CreateDirectory(presetPath);

        await File.WriteAllTextAsync(filePath, builder.BuildJson());

        return new PresetExportResult
        {
            FilePath = filePath,
            CarCount = builder.Count
        };
    }

    public async Task<IEnumerable<RaceResultFileModel>> GetRaceResultFilesAsync()
    {
        var config = await _configRepository.GetAsync();
        var path = config?.RaceResultsPath?.Trim();

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return Enumerable.Empty<RaceResultFileModel>();
        }

        return Directory.GetFiles(path, "*.json")
            .Select(f => new { FileName = Path.GetFileName(f), FilePath = f })
            .Where(f => TryParseSessionDate(f.FileName, out _))
            .OrderByDescending(f => f.FileName)
            .Select(f =>
            {
                TryParseSessionDate(f.FileName, out var date);
                var track = ReadSessionInfo(f.FilePath);
                return new RaceResultFileModel
                {
                    FileName = f.FileName,
                    SessionDate = date,
                    Track = track ?? string.Empty
                };
            })
            .Where(f => !string.IsNullOrWhiteSpace(f.Track))
            .ToList();
    }

    public async Task<RaceResultImportResult> ImportRaceResultAsync(Guid raceId, string fileName)
    {
        var config = await _configRepository.GetAsync();
        var path = config?.RaceResultsPath?.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Race results path is not configured. Set it on the Game Config page.");
        }

        var filePath = Path.Combine(path, fileName);

        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"Result file not found: {fileName}");
        }

        CmSessionFile? sessionFile;

        try
        {
            await using var stream = File.OpenRead(filePath);
            sessionFile = await JsonSerializer.DeserializeAsync<CmSessionFile>(stream, CaseInsensitiveJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The selected file is not a valid Content Manager session file: {fileName}",
                ex);
        }

        if (sessionFile is null || sessionFile.Sessions.Length == 0)
        {
            throw new InvalidOperationException("The selected file contains no sessions.");
        }

        var raceSession = sessionFile.Sessions.FirstOrDefault(s => s is { Type: 3 });

        if (raceSession is null)
        {
            throw new InvalidOperationException("The selected file contains no race session to import.");
        }

        var race = await _raceRepository.GetByIdAsync(raceId);

        if (race is null)
        {
            throw new InvalidOperationException("Race not found.");
        }

        var pointSetting = await _pointSettingRepository.GetByIdAsync(race.PointSettingId);
        var teams = (await _raceRepository.GetEnteredTeamsWithDriversAsync(raceId)).ToList();

        var driverByKey = new Dictionary<string, (Driver Driver, Team Team)>(StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            foreach (var driver in team.Drivers.Where(d => d.IsActive))
            {
                driverByKey[BuildDriverKey(driver.Car, driver.Skin, driver.DriverName)] = (driver, team);
            }
        }

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

            entries.Add(new ImportEntry
            {
                Match = match,
                BestLapMs = bestLapMs,
                LapCount = lapCount
            });
        }

        var session = new Session
        {
            SessionId = Guid.NewGuid(),
            SessionType = SessionType.Race,
            SessionDate = TryParseSessionDate(fileName, out var date) ? date : DateTime.Now,
            RaceId = raceId,
            Race = race,
            DriverStandings = new List<DriverStanding>()
        };

        var ordered = entries
            .GroupBy(e => e.Match.Team.TeamClass?.TeamClassName?.Trim() ?? string.Empty)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g =>
            {
                var classMatches = g
                    .OrderByDescending(e => e.LapCount)
                    .ThenBy(e => e.BestLapMs)
                    .ToList();

                return classMatches.Select((e, index) => new
                {
                    Entry = e,
                    ClassPosition = index + 1,
                    TeamClassName = g.Key
                });
            })
            .ToList();

        foreach (var item in ordered)
        {
            session.DriverStandings.Add(new DriverStanding
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
                Points = pointSetting?.Config.TryGetValue(item.ClassPosition.ToString(), out var points) == true ? points : 0,
                BestLapTimeMs = item.Entry.BestLapMs,
                LapCount = item.Entry.LapCount
            });
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

        var race = await _raceRepository.GetByIdAsync(raceId);

        if (race is null)
        {
            throw new InvalidOperationException("Race not found.");
        }

        var teams = (await _raceRepository.GetEnteredTeamsWithDriversAsync(raceId)).ToList();

        var driverByKey = new Dictionary<string, (Driver Driver, Team Team)>(StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            foreach (var driver in team.Drivers.Where(d => d.IsActive))
            {
                driverByKey[BuildDriverKey(driver.Car, driver.Skin, driver.DriverName)] = (driver, team);
            }
        }

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

        var untimed = new List<ImportEntry>();

        var timedKeys = timed.Select(e => BuildDriverKey(e.Match.Driver.Car, e.Match.Driver.Skin, e.Match.Driver.DriverName)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            foreach (var driver in team.Drivers.Where(d => d.IsActive))
            {
                var key = BuildDriverKey(driver.Car, driver.Skin, driver.DriverName);

                if (!timedKeys.Contains(key))
                {
                    untimed.Add(new ImportEntry
                    {
                        Match = (driver, team),
                        BestLapMs = 0,
                        LapCount = 0
                    });
                }
            }
        }

        var orderedUntimed = untimed
            .OrderBy(e => e.Match.Team.TeamName)
            .ThenBy(e => e.Match.Driver.DriverName)
            .ToList();
        var orderedEntries = timed.Concat(orderedUntimed);

        var session = new Session
        {
            SessionId = Guid.NewGuid(),
            SessionType = sessionType,
            SessionDate = TryParseSessionDate(fileName ?? string.Empty, out var date) ? date : DateTime.Now,
            RaceId = raceId,
            Race = race,
            DriverStandings = new List<DriverStanding>()
        };

        var ordered = orderedEntries
            .GroupBy(e => e.Match.Team.TeamClass?.TeamClassName?.Trim() ?? string.Empty)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g =>
            {
                var classMatches = g
                    .OrderByDescending(e => e.BestLapMs > 0)
                    .ThenBy(e => e.BestLapMs)
                    .ToList();

                return classMatches.Select((e, index) => new
                {
                    Entry = e,
                    ClassPosition = index + 1,
                    TeamClassName = g.Key
                });
            })
            .ToList();

        foreach (var item in ordered)
        {
            session.DriverStandings.Add(new DriverStanding
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
                Points = 0,
                BestLapTimeMs = item.Entry.BestLapMs,
                LapCount = 0
            });
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

    private sealed class ImportEntry
    {
        public required (Driver Driver, Team Team) Match { get; set; }

        public int BestLapMs { get; set; }

        public int LapCount { get; set; }
    }

    private static bool TryParseSessionDate(string fileName, out DateTime date)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        return DateTime.TryParseExact(
            name,
            "yyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out date);
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
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed))
        {
            return false;
        }

        lapTimeMs = (int)Math.Round(parsed.TotalMilliseconds);
        return lapTimeMs > 0;
    }

    private static string? ReadSessionInfo(string filePath)
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

    private static string BuildDriverKey(string car, string skin, string driverName)
    {
        return car + "\u001F" + skin + "\u001F" + (driverName ?? string.Empty).Trim();
    }

    private static string SessionTypeLabel(SessionType sessionType)
    {
        return sessionType switch
        {
            SessionType.Practice => "Practice",
            SessionType.Qualifying => "Qualifying",
            _ => "Race"
        };
    }

    private static int ClassOrderIndex(string? teamClassName)
    {
        var normalized = teamClassName?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "hypercar" or "h" or "lmh" => 0,
            "lmp2" => 1,
            "gt3" or "gts" or "lmgt" => 2,
            _ => 3
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Trim();
    }

    private sealed class SessionResultEntry
    {
        public string Driver { get; set; } = string.Empty;

        public string Car { get; set; } = string.Empty;

        public string Skin { get; set; } = string.Empty;

        public string? BestLapTimeMs { get; set; }
    }

    private sealed class CmSessionFile
    {
        public string Track { get; set; } = string.Empty;

        public int Number_Of_Sessions { get; set; }

        public CmPlayer[] Players { get; set; } = Array.Empty<CmPlayer>();

        public CmSession[] Sessions { get; set; } = Array.Empty<CmSession>();
    }

    private sealed class CmPlayer
    {
        public string Name { get; set; } = string.Empty;

        public string Car { get; set; } = string.Empty;

        public string Skin { get; set; } = string.Empty;
    }

    private sealed class CmSession
    {
        public int Event { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Type { get; set; }

        public int LapsCount { get; set; }

        public int Duration { get; set; }

        public List<CmLap> Laps { get; set; } = new();

        public List<int> LapsTotal { get; set; } = new();

        public List<CmBestLap> BestLaps { get; set; } = new();

        public List<int>? RaceResult { get; set; }
    }

    private sealed class CmLap
    {
        public int Lap { get; set; }

        public int Car { get; set; }

        public double Time { get; set; }
    }

    private sealed class CmBestLap
    {
        public int Car { get; set; }

        public long Time { get; set; }

        public int Lap { get; set; }
    }
}