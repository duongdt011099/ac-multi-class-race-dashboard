using MulticlassRace.Mapping;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class RaceService : IRaceService
{
    private readonly IRaceRepository _raceRepository;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IPointSettingRepository _pointSettingRepository;
    private readonly IClassSettingRepository _classSettingRepository;
    private readonly ITrackService _trackService;

    public RaceService(
        IRaceRepository raceRepository,
        ISeasonRepository seasonRepository,
        IPointSettingRepository pointSettingRepository,
        IClassSettingRepository classSettingRepository,
        ITrackService trackService)
    {
        _raceRepository = raceRepository;
        _seasonRepository = seasonRepository;
        _pointSettingRepository = pointSettingRepository;
        _classSettingRepository = classSettingRepository;
        _trackService = trackService;
    }

    public async Task<IEnumerable<RaceModel>> GetRacesBySeasonAsync(Guid seasonId)
    {
        var races = await _raceRepository.GetRacesBySeasonAsync(seasonId);

        return races.Select(r => r.ToModel());
    }

    public async Task<RaceModel?> GetRaceByIdAsync(Guid raceId)
    {
        var race = await _raceRepository.GetRaceWithClassSettingAsync(raceId);

        return race?.ToModel();
    }

    public async Task<IEnumerable<RaceModel>> GetCandidateRacesAsync(string? trackName, SessionType sessionType)
    {
        // Race results replace a race session and may target a race already marked Finished.
        // Practice and Qualifying choices remain limited to unfinished races.
        var canonicalTrackName = await _trackService.ResolveTrackNameAsync(trackName);
        var races = await _raceRepository.GetCandidateRacesAsync(
            canonicalTrackName,
            includeFinished: sessionType == SessionType.Race);

        return races.Select(r => r.ToModel());
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

        var classSetting = await _classSettingRepository.GetByIdWithItemsAsync(model.ClassSettingId);

        if (classSetting is null || classSetting.Items.Count == 0)
        {
            throw new InvalidOperationException("A class setting with at least one class must be selected.");
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
            ClassSettingId = classSetting.ClassSettingId,
            ClassSetting = classSetting,
            Season = season,
            Sessions = new List<Session>(),
            EnteredTeams = new List<Team>()
        };

        await _raceRepository.AddAsync(race);
    }

    public async Task UpdateRaceAsync(RaceFormModel model)
    {
        var race = await _raceRepository.GetRaceWithClassSettingAsync(model.RaceId);

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

        if (model.ClassSettingId != Guid.Empty && race.ClassSettingId != model.ClassSettingId)
        {
            var classSetting = await _classSettingRepository.GetByIdWithItemsAsync(model.ClassSettingId);

            if (classSetting is not null)
            {
                race.ClassSettingId = classSetting.ClassSettingId;
                race.ClassSetting = classSetting;
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

        return sessions.Select(s => s.ToModel());
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

        return teams.Select(t => t.ToModel());
    }

    public async Task SaveRaceTeamsAsync(Guid raceId, IReadOnlyCollection<Guid> teamIds)
    {
        await _raceRepository.UpdateRaceTeamsAsync(raceId, teamIds);
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
            if (layouts.Any(string.IsNullOrWhiteSpace))
            {
                // Tracks with extra layouts can still expose their root/default layout. It is
                // selected as a blank CONFIG_TRACK value, just like a single-layout track.
                return;
            }

            throw new InvalidOperationException("Select a track layout.");
        }

        if (!layouts.Contains(trackLayout.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Select a valid layout for the chosen track.");
        }
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int NormalizeSessionMinutes(int? minutes, int min)
    {
        return Math.Clamp(minutes ?? 20, min, 90);
    }
}
