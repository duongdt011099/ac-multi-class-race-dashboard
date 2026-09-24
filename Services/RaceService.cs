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
                .OrderBy(d => d.Position)
                .Select(d => new DriverStandingModel
                {
                    Position = d.Position,
                    DriverName = d.Driver.DriverName,
                    TeamName = d.TeamName,
                    BestLapTimeMs = d.BestLapTimeMs,
                    Points = d.Points
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

    private static string SessionTypeLabel(SessionType sessionType)
    {
        return sessionType switch
        {
            SessionType.Practice => "Practice",
            SessionType.Qualifying => "Qualifying",
            _ => "Race"
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Trim();
    }
}