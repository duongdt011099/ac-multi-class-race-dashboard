using MulticlassRace.Models;
using MulticlassRace.Services;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.Support;

/// <summary>In-memory races and sessions, so race page tests do not need Assetto Corsa or Content Manager.</summary>
public sealed class FakeRaceService : IRaceService
{
    public Dictionary<Guid, List<RaceModel>> RacesBySeason { get; } = new();

    public Dictionary<Guid, HashSet<Guid>> TeamsByRace { get; } = new();

    public Dictionary<Guid, List<SessionModel>> SessionsByRace { get; } = new();

    public List<RaceFormModel> Created { get; } = new();

    public List<RaceFormModel> Updated { get; } = new();

    public List<Guid> Deleted { get; } = new();

    public List<(Guid RaceId, SessionType Type, string Weather, double SunAngle, bool Penalties)> Launched { get; } = new();

    public ResultImportAvailabilityModel Availability { get; set; } = new();

    public List<RaceResultFileModel> ResultFiles { get; } = new();

    public RaceModel AddRace(Guid seasonId, string name, string? track = "monza", Guid? classSettingId = null)
    {
        var race = new RaceModel
        {
            RaceId = Guid.NewGuid(),
            RaceName = name,
            TrackName = track,
            ClassSettingId = classSettingId ?? Guid.Empty,
            PointSettingName = "Standard",
            ClassSettingName = "WEC",
            PracticeSessionMinutes = 20
        };

        if (!RacesBySeason.TryGetValue(seasonId, out var list))
        {
            RacesBySeason[seasonId] = list = new List<RaceModel>();
        }

        list.Add(race);
        return race;
    }

    public Task<IEnumerable<RaceModel>> GetRacesBySeasonAsync(Guid seasonId)
        => Task.FromResult<IEnumerable<RaceModel>>(RacesBySeason.TryGetValue(seasonId, out var list) ? list.ToList() : new List<RaceModel>());

    public Task<RaceModel?> GetRaceByIdAsync(Guid raceId)
        => Task.FromResult(RacesBySeason.Values.SelectMany(r => r).FirstOrDefault(r => r.RaceId == raceId));

    public Task<IEnumerable<RaceModel>> GetCandidateRacesAsync(string? trackName, SessionType sessionType) => throw new NotSupportedException();

    public Task CreateRaceAsync(RaceFormModel model)
    {
        Created.Add(model);
        AddRace(model.SeasonId, model.RaceName, model.TrackName, model.ClassSettingId);
        return Task.CompletedTask;
    }

    public Task UpdateRaceAsync(RaceFormModel model)
    {
        Updated.Add(model);
        var race = RacesBySeason.Values.SelectMany(r => r).First(r => r.RaceId == model.RaceId);
        race.RaceName = model.RaceName;
        race.TrackName = model.TrackName;
        return Task.CompletedTask;
    }

    public Task DeleteRaceAsync(Guid raceId)
    {
        Deleted.Add(raceId);

        foreach (var list in RacesBySeason.Values)
        {
            list.RemoveAll(r => r.RaceId == raceId);
        }

        return Task.CompletedTask;
    }

    public Task LaunchSessionAsync(Guid raceId, SessionType sessionType, string weather, double sunAngle, bool penalties = true)
    {
        Launched.Add((raceId, sessionType, weather, sunAngle, penalties));
        return Task.CompletedTask;
    }

    public Task<IEnumerable<SessionModel>> GetRaceSessionsAsync(Guid raceId)
        => Task.FromResult<IEnumerable<SessionModel>>(SessionsByRace.TryGetValue(raceId, out var list) ? list.ToList() : new List<SessionModel>());

    public Task<IEnumerable<TeamModel>> GetRaceTeamsAsync(Guid raceId)
    {
        var ids = TeamsByRace.TryGetValue(raceId, out var set) ? set : new HashSet<Guid>();
        return Task.FromResult<IEnumerable<TeamModel>>(ids.Select(id => new TeamModel { TeamId = id, TeamName = id.ToString() }).ToList());
    }

    public Task<int> GetEnteredCarCountAsync(Guid raceId)
        => Task.FromResult(TeamsByRace.TryGetValue(raceId, out var set) ? set.Count : 0);

    public Task SaveRaceTeamsAsync(Guid raceId, IReadOnlyCollection<Guid> teamIds)
    {
        TeamsByRace[raceId] = teamIds.ToHashSet();
        return Task.CompletedTask;
    }

    public Task<PresetExportResult> ExportGridPresetAsync(Guid raceId, SessionType sessionType, bool humanClassOnly, string seasonName, string championshipName)
        => throw new NotSupportedException();

    public Task<PresetExportResult> ExportRaceGridPresetAsync(Guid raceId, string seasonName, string championshipName)
        => throw new NotSupportedException();

    public Task<IEnumerable<RaceResultFileModel>> GetRaceResultFilesAsync()
        => Task.FromResult<IEnumerable<RaceResultFileModel>>(ResultFiles.ToList());

    public Task<ResultImportAvailabilityModel> GetResultImportAvailabilityAsync() => Task.FromResult(Availability);

    public Task<RaceResultImportResult> ImportRaceResultAsync(Guid raceId, string fileName) => throw new NotSupportedException();

    public Task<RaceResultImportResult> ImportRaceResultFromPathAsync(Guid raceId, string fullPath, string? fileName) => throw new NotSupportedException();

    public Task<RaceResultImportResult> ImportSessionResultAsync(Guid raceId, SessionType sessionType, Stream stream, string? fileName)
        => throw new NotSupportedException();
}

public sealed class FakeTrackService : ITrackService
{
    public List<TrackOptionModel> Tracks { get; } = new()
    {
        new TrackOptionModel("monza", "Monza"),
        new TrackOptionModel("spa", "Spa-Francorchamps")
    };

    public Dictionary<string, List<TrackLayoutOptionModel>> Layouts { get; } = new()
    {
        ["spa"] = new() { new TrackLayoutOptionModel("gp", "Grand Prix"), new TrackLayoutOptionModel("short", "Short") }
    };

    public int? BestLapMs { get; set; } = 90_000;

    public int? PitCount { get; set; } = 30;

    public Task<IReadOnlyList<TrackOptionModel>> GetTrackOptionsAsync() => Task.FromResult<IReadOnlyList<TrackOptionModel>>(Tracks.ToList());

    public Task<string> GetTrackDisplayNameAsync(string? trackName)
        => Task.FromResult(Tracks.FirstOrDefault(t => t.TrackId == trackName)?.DisplayName ?? trackName ?? string.Empty);

    public Task<string?> ResolveTrackNameAsync(string? trackName) => Task.FromResult(trackName);

    public Task<IReadOnlyList<string>> GetTrackLayoutsAsync(string trackName)
        => Task.FromResult<IReadOnlyList<string>>(GetLayouts(trackName).Select(l => l.LayoutId).ToList());

    public Task<IReadOnlyList<TrackLayoutOptionModel>> GetTrackLayoutOptionsAsync(string trackName)
        => Task.FromResult<IReadOnlyList<TrackLayoutOptionModel>>(GetLayouts(trackName));

    public Task<int?> GetBestLapTimeAsync(string trackName, string? layout) => Task.FromResult(BestLapMs);

    public Task<int?> GetPitCountAsync(string trackName, string? layout) => Task.FromResult(PitCount);

    private List<TrackLayoutOptionModel> GetLayouts(string track)
        => Layouts.TryGetValue(track, out var layouts) ? layouts.ToList() : new List<TrackLayoutOptionModel>();
}

public sealed class FakeLuaResultImportService : ILuaResultImportService
{
    public Task<LuaResultImportOutcome> ImportAsync(PendingLuaResult result, Guid raceId)
        => Task.FromResult(new LuaResultImportOutcome(true, "ok", false));

    public Task RecordDeclinedAsync(PendingLuaResult result) => Task.CompletedTask;

    public Task RecordManualImportAsync(string fullPath, string fileName) => Task.CompletedTask;
}
