using MulticlassRace.Builders;
using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;

namespace MulticlassRace.Services;

/// <summary>Builds the grid and race.ini for a session and hands it to Assetto Corsa.</summary>
public class RaceSessionLauncher : IRaceSessionLauncher
{
    private readonly IRaceRepository _raceRepository;
    private readonly ITrackService _trackService;
    private readonly IGameSessionLauncher _gameSessionLauncher;
    private readonly SessionLaunchTracker _launchTracker;
    private readonly AssettoCorsaPathResolver _pathResolver;

    public RaceSessionLauncher(
        IRaceRepository raceRepository,
        ITrackService trackService,
        IGameSessionLauncher gameSessionLauncher,
        SessionLaunchTracker launchTracker,
        AssettoCorsaPathResolver pathResolver)
    {
        _raceRepository = raceRepository;
        _trackService = trackService;
        _gameSessionLauncher = gameSessionLauncher;
        _launchTracker = launchTracker;
        _pathResolver = pathResolver;
    }

    public async Task LaunchSessionAsync(
        Guid raceId,
        SessionType sessionType,
        string weather,
        double sunAngle,
        bool penalties = true)
    {
        var race = await _raceRepository.GetRaceWithClassSettingAsync(raceId);

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

        var gamePath = (await _pathResolver.GetAsync()).Path;

        if (string.IsNullOrWhiteSpace(gamePath))
        {
            throw new InvalidOperationException("The Assetto Corsa folder is not configured. Reinstall the dashboard and point it at the game.");
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

        var classOrderByItemId = RaceClassOrdering.OrderById(race);

        var drivers = teams
            .SelectMany(t => t.Drivers.Where(d => d.IsActive))
            .OrderBy(d => RaceClassOrdering.IndexOf(RaceClassOrdering.ClassItemOf(race, d.Team), classOrderByItemId))
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
                .OrderBy(d => RaceClassOrdering.IndexOf(RaceClassOrdering.ClassItemOf(race, d.Team), classOrderByItemId))
                .ThenBy(d => qualifyingPositionByDriverId.TryGetValue(d.DriverId, out var pos) ? pos : int.MaxValue)
                .ThenBy(d => d.Team?.TeamName)
                .ThenBy(d => d.DriverName)
                .ToList();

            playerStartPosition = human is null
                ? 1
                : drivers.TakeWhile(d => d.DriverId != human.DriverId).Count() + 1;
        }

        var rollingStartLapCompensation = sessionType == SessionType.Race &&
            HasTrackAiSpline(gamePath, race.TrackName, race.TrackLayout);

        var raceIni = new RaceIniBuilder().Build(
            race,
            sessionType,
            human,
            drivers,
            weather,
            sunAngle,
            playerStartPosition,
            penalties,
            rollingStartLapCompensation);

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

    private static bool HasTrackAiSpline(string gamePath, string? trackName, string? layout)
    {
        if (!IsSafeFolderSegment(trackName) ||
            (!string.IsNullOrWhiteSpace(layout) && !IsSafeFolderSegment(layout)))
        {
            return false;
        }

        try
        {
            var tracksRoot = Path.GetFullPath(Path.Combine(gamePath, "content", "tracks"));
            var trackRoot = Path.GetFullPath(Path.Combine(tracksRoot, trackName!.Trim()));

            if (!IsChildPath(tracksRoot, trackRoot))
            {
                return false;
            }

            var aiDirectories = new List<string>();

            if (!string.IsNullOrWhiteSpace(layout))
            {
                var layoutRoot = Path.GetFullPath(Path.Combine(trackRoot, layout.Trim()));

                if (IsChildPath(trackRoot, layoutRoot))
                {
                    aiDirectories.Add(Path.Combine(layoutRoot, "ai"));
                }
            }

            aiDirectories.Add(Path.Combine(trackRoot, "ai"));

            return aiDirectories.Any(directory => File.Exists(Path.Combine(directory, "fast_lane.ai")));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSafeFolderSegment(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Trim() is not "." and not ".." &&
               !value.Contains('\\') &&
               !value.Contains('/');
    }

    private static bool IsChildPath(string root, string candidate)
    {
        return candidate.StartsWith(
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
