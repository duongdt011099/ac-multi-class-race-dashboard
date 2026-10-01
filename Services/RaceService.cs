using System.Text.Json;
using MulticlassRace.Builders;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.Services.ImportModels;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class RaceService : IRaceService
{
    private readonly IRaceRepository _raceRepository;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IAssettoCorsaGameConfigRepository _configRepository;
    private readonly IPointSettingRepository _pointSettingRepository;
    private readonly ITrackService _trackService;
    private readonly IGameSessionLauncher _gameSessionLauncher;
    private readonly SessionLaunchTracker _launchTracker;

    private static readonly JsonSerializerOptions CaseInsensitiveJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RaceService(
        IRaceRepository raceRepository,
        ISeasonRepository seasonRepository,
        IAssettoCorsaGameConfigRepository configRepository,
        IPointSettingRepository pointSettingRepository,
        ITrackService trackService,
        IGameSessionLauncher gameSessionLauncher,
        SessionLaunchTracker launchTracker)
    {
        _raceRepository = raceRepository;
        _seasonRepository = seasonRepository;
        _configRepository = configRepository;
        _pointSettingRepository = pointSettingRepository;
        _trackService = trackService;
        _gameSessionLauncher = gameSessionLauncher;
        _launchTracker = launchTracker;
    }

    private static RaceModel MapRace(Race r)
    {
        return new RaceModel
        {
            RaceId = r.RaceId,
            RaceName = r.RaceName,
            Country = r.Country,
            Status = r.Status,
            PointSettingId = r.PointSettingId,
            PointSettingName = r.PointSetting.SettingName,
            TrackName = r.TrackName,
            TrackLayout = r.TrackLayout,
            NumberOfLaps = r.NumberOfLaps,
            RaceDuration = r.RaceDuration,
            PracticeSessionMinutes = r.PracticeSessionMinutes,
            QualifyingSessionMinutes = r.QualifyingSessionMinutes,
            StandingCount = r.Sessions.Sum(s => s.DriverStandings.Count)
        };
    }

    public async Task<IEnumerable<RaceModel>> GetRacesBySeasonAsync(Guid seasonId)
    {
        var races = await _raceRepository.GetRacesBySeasonAsync(seasonId);

        return races.Select(MapRace);
    }

    public async Task<RaceModel?> GetRaceByIdAsync(Guid raceId)
    {
        var race = await _raceRepository.GetByIdAsync(raceId);

        return race is null ? null : MapRace(race);
    }

    public async Task<IEnumerable<RaceModel>> GetCandidateRacesAsync(string? trackName, SessionType sessionType)
    {
        // Race results replace a race session and may target a race already marked Finished.
        // Practice and Qualifying choices remain limited to unfinished races.
        var canonicalTrackName = await _trackService.ResolveTrackNameAsync(trackName);
        var races = await _raceRepository.GetCandidateRacesAsync(
            canonicalTrackName,
            includeFinished: sessionType == SessionType.Race);

        return races.Select(MapRace);
    }

    public async Task CreateRaceAsync(RaceFormModel model)
    {
        var season = await _seasonRepository.GetByIdAsync(model.SeasonId);

        if (season is null)
        {
            throw new InvalidOperationException("Season not found.");
        }

        var pointSetting = await _pointSettingRepository.GetByIdAsync(model.PointSettingId);

        if (pointSetting is null)
        {
            throw new InvalidOperationException("A point setting must be selected.");
        }

        await ValidateRaceTrackAsync(model.TrackName, model.TrackLayout);

        var race = new Race
        {
            RaceId = Guid.NewGuid(),
            RaceName = model.RaceName.Trim(),
            Country = model.Country?.Trim() ?? string.Empty,
            Status = RaceStatus.NotStarted,
            TrackName = NormalizeNullable(model.TrackName),
            TrackLayout = NormalizeNullable(model.TrackLayout),
            NumberOfLaps = model.NumberOfLaps,
            RaceDuration = model.RaceDuration,
            PracticeSessionMinutes = NormalizeSessionMinutes(model.PracticeSessionMinutes, min: 0),
            QualifyingSessionMinutes = NormalizeSessionMinutes(model.QualifyingSessionMinutes, min: 5),
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

        await ValidateRaceTrackAsync(model.TrackName, model.TrackLayout);

        race.RaceName = model.RaceName.Trim();
        race.Country = model.Country?.Trim() ?? string.Empty;
        race.TrackName = NormalizeNullable(model.TrackName);
        race.TrackLayout = NormalizeNullable(model.TrackLayout);
        race.NumberOfLaps = model.NumberOfLaps;
        race.RaceDuration = model.RaceDuration;
        race.PracticeSessionMinutes = NormalizeSessionMinutes(model.PracticeSessionMinutes, min: 0);
        race.QualifyingSessionMinutes = NormalizeSessionMinutes(model.QualifyingSessionMinutes, min: 5);

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

    public async Task LaunchSessionAsync(
        Guid raceId,
        SessionType sessionType,
        string weather,
        double sunAngle,
        bool penalties = true)
    {
        var race = await _raceRepository.GetByIdAsync(raceId);

        if (race is null)
        {
            throw new InvalidOperationException("Race not found.");
        }

        if (string.IsNullOrWhiteSpace(race.TrackName))
        {
            throw new InvalidOperationException("Select a track for this race before starting a session.");
        }

        if (sessionType == SessionType.Race && race.NumberOfLaps is not > 0)
        {
            throw new InvalidOperationException("Set the number of laps for this race before starting the session.");
        }

        if (sessionType == SessionType.Practice && race.PracticeSessionMinutes == 0)
        {
            throw new InvalidOperationException("Practice is disabled for this race because its duration is set to 0 minutes.");
        }

        var config = await _configRepository.GetAsync();
        var gamePath = config?.GamePath?.Trim();

        if (string.IsNullOrWhiteSpace(gamePath))
        {
            throw new InvalidOperationException("Game path is not configured. Set it on the Game Config page.");
        }

        var teams = (await _raceRepository.GetEnteredTeamsWithDriversAsync(raceId)).ToList();

        var activeDriverCount = teams.Sum(t => t.Drivers.Count(d => d.IsActive));
        var pitCount = await _trackService.GetPitCountAsync(
            race.TrackName,
            string.IsNullOrWhiteSpace(race.TrackLayout) ? null : race.TrackLayout);

        if (pitCount is > 0 && activeDriverCount > pitCount.Value)
        {
            throw new InvalidOperationException(
                $"The number of cars ({activeDriverCount}) is more than the number of pits ({pitCount}). " +
                "Please change the race track or layout.");
        }

        var drivers = teams
            .SelectMany(t => t.Drivers.Where(d => d.IsActive))
            .OrderBy(d => d.Team?.TeamClass?.TeamClassName)
            .ThenBy(d => d.Team?.TeamName)
            .ThenBy(d => d.DriverName)
            .ToList();

        var human = drivers.FirstOrDefault(d => d.IsHuman);

        var playerStartPosition = 1;

        if (sessionType == SessionType.Race)
        {
            var qualifying = (await _raceRepository.GetSessionsByRaceAsync(raceId))
                .Where(s => s.SessionType == SessionType.Qualifying && s.DriverStandings.Any(d => d.Driver != null))
                .OrderByDescending(s => s.SessionDate)
                .FirstOrDefault();

            if (qualifying is null)
            {
                throw new InvalidOperationException(
                    "A qualifying session with results is required before starting the race.");
            }

            var qualifyingPositionByDriverId = qualifying.DriverStandings
                .Where(d => d.Driver != null)
                .GroupBy(d => d.Driver!.DriverId)
                .ToDictionary(g => g.Key, g => g.Min(d => d.ClassPosition));

            drivers = drivers
                .OrderBy(d => ClassOrderIndex(d.Team?.TeamClass?.TeamClassName))
                .ThenBy(d => qualifyingPositionByDriverId.TryGetValue(d.DriverId, out var pos) ? pos : int.MaxValue)
                .ThenBy(d => d.Team?.TeamName)
                .ThenBy(d => d.DriverName)
                .ToList();

            playerStartPosition = human is null
                ? 1
                : drivers.TakeWhile(d => d.DriverId != human.DriverId).Count() + 1;
        }

        var raceIni = new RaceIniBuilder().Build(
            race,
            sessionType,
            human,
            drivers,
            weather,
            sunAngle,
            playerStartPosition,
            penalties);

        var executable = Path.Combine(gamePath, "acs.exe");

        if (!File.Exists(executable))
        {
            executable = Path.Combine(gamePath, "AssettoCorsa.exe");
        }

        if (!File.Exists(executable))
        {
            throw new InvalidOperationException(
                $"Assetto Corsa executable not found in the configured game path: {gamePath}");
        }

        var seasonName = string.Empty;
        var championshipName = string.Empty;

        try
        {
            seasonName = race.Season?.SeasonName ?? string.Empty;
            championshipName = race.Season?.Championship?.ChampionshipName ?? string.Empty;
        }
        catch
        {
            // Season/Championship are not eagerly loaded here; the context is optional.
        }

        await _gameSessionLauncher.LaunchAsync(gamePath, executable, raceIni);

        _launchTracker.Record(new SessionLaunch(
            raceId,
            sessionType,
            race.RaceName,
            seasonName,
            championshipName,
            race.TrackName ?? string.Empty,
            DateTimeOffset.UtcNow));
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task ValidateRaceTrackAsync(string? trackName, string? trackLayout)
    {
        if (string.IsNullOrWhiteSpace(trackName))
        {
            throw new InvalidOperationException("Select a track.");
        }

        var layouts = (await _trackService.GetTrackLayoutsAsync(trackName.Trim())).ToList();
        if (layouts.Count == 0)
        {
            // Single-layout tracks use AC's default layout, represented by an empty value.
            return;
        }

        if (string.IsNullOrWhiteSpace(trackLayout))
        {
            throw new InvalidOperationException("Select a track layout.");
        }

        if (!layouts.Contains(trackLayout.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Select a valid layout for the chosen track.");
        }
    }

    private static int NormalizeSessionMinutes(int? minutes, int min)
    {
        return Math.Clamp(minutes ?? 20, min, 90);
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

    public async Task<int> GetEnteredCarCountAsync(Guid raceId)
    {
        var teams = await _raceRepository.GetEnteredTeamsWithDriversAsync(raceId);

        return teams
            .SelectMany(t => t.Drivers)
            .Count(d => d.IsActive);
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
            if (row.Driver.IsHuman && (sessionType == SessionType.Practice || sessionType == SessionType.Qualifying)) continue;
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
                continue;
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

        var contentManagerFiles = new List<RaceResultFileModel>();

        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            contentManagerFiles.AddRange(Directory
                .GetFiles(path, "*" + LuaResultPaths.FileExtension)
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
                        FullPath = f.FilePath,
                        SessionDate = date,
                        Track = track ?? string.Empty,
                        IsLuaResult = false
                    };
                })
                .Where(f => !string.IsNullOrWhiteSpace(f.Track)));
        }

        // Lua-produced race results live in their own subfolder so a declined or failed
        // auto-import can still be retried through the same manual dropdown.
        var luaRaceFolder = Path.Combine(LuaResultPaths.ResolveRoot(config?.LuaResultPath), LuaResultPaths.RaceFolder);
        var luaFiles = new List<RaceResultFileModel>();

        if (Directory.Exists(luaRaceFolder))
        {
            luaFiles.AddRange(Directory
                .GetFiles(luaRaceFolder, "*" + LuaResultPaths.FileExtension)
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
                        FullPath = f.FilePath,
                        SessionDate = date,
                        Track = track ?? string.Empty,
                        IsLuaResult = true
                    };
                })
                .Where(f => !string.IsNullOrWhiteSpace(f.Track)));
        }

        return contentManagerFiles
            .Concat(luaFiles)
            .OrderByDescending(f => f.SessionDate)
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

        return await ImportRaceResultFromPathAsync(raceId, filePath, fileName);
    }

    public async Task<RaceResultImportResult> ImportRaceResultFromPathAsync(Guid raceId, string fullPath, string? fileName)
    {
        var resolvedName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(fullPath) : fileName.Trim();

        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Result file not found: {resolvedName}");
        }

        CmSessionFile? sessionFile;

        try
        {
            await using var stream = File.OpenRead(fullPath);
            sessionFile = await JsonSerializer.DeserializeAsync<CmSessionFile>(stream, CaseInsensitiveJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The selected file is not a valid Content Manager session file: {resolvedName}",
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
            SessionDate = TryParseSessionDate(resolvedName, out var date) ? date : DateTime.Now,
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

}
