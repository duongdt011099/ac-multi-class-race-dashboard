using MulticlassRace.Builders;
using MulticlassRace.Models;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Drivers;
using MulticlassRace.Services.Races;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.QuickRace;

/// <summary>
/// Starts a quick race: builds race.ini and the session file from the saved quick race grid and hands
/// them to Assetto Corsa, the same way a championship race session is started.
/// </summary>
public class QuickRaceLauncher : IQuickRaceLauncher
{
    private readonly IQuickRaceService _quickRaceService;
    private readonly IQuickRaceRepository _repository;
    private readonly IDriverService _driverService;
    private readonly IAssettoCorsaContentService _contentService;
    private readonly ITrackService _trackService;
    private readonly IGameSessionLauncher _gameSessionLauncher;
    private readonly SessionLaunchTracker _launchTracker;
    private readonly AssettoCorsaPathResolver _pathResolver;
    private readonly IAssettoCorsaGameConfigService? _configService;
    private readonly ISteamStatusService? _steamStatus;

    public QuickRaceLauncher(
        IQuickRaceService quickRaceService,
        IQuickRaceRepository repository,
        IDriverService driverService,
        IAssettoCorsaContentService contentService,
        ITrackService trackService,
        IGameSessionLauncher gameSessionLauncher,
        SessionLaunchTracker launchTracker,
        AssettoCorsaPathResolver pathResolver,
        IAssettoCorsaGameConfigService? configService = null,
        ISteamStatusService? steamStatus = null)
    {
        _quickRaceService = quickRaceService;
        _repository = repository;
        _driverService = driverService;
        _contentService = contentService;
        _trackService = trackService;
        _gameSessionLauncher = gameSessionLauncher;
        _launchTracker = launchTracker;
        _pathResolver = pathResolver;
        _configService = configService;
        _steamStatus = steamStatus;
    }

    public async Task LaunchAsync(SessionType sessionType = SessionType.Race)
    {
        var setup = await _quickRaceService.GetSetupAsync();
        var settings = setup.Settings;

        var humanDriver = (await _driverService.GetActiveDriversAsync()).FirstOrDefault(d => d.IsHuman);
        var human = QuickRaceGrid.ResolveHuman(settings, humanDriver, await _contentService.GetCarOptionsAsync());

        if (_steamStatus is { } steam && !steam.IsRunning())
        {
            throw new InvalidOperationException("Steam is not running. Start Steam before starting a session.");
        }

        var layouts = string.IsNullOrWhiteSpace(settings.TrackName)
            ? new List<TrackLayoutOptionModel>()
            : (await _trackService.GetTrackLayoutOptionsAsync(settings.TrackName)).ToList();

        var problem = QuickRaceGrid.ValidateSettings(settings, layouts) ?? QuickRaceGrid.ValidateGrid(setup, human);

        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }

        var gamePath = (await _pathResolver.GetAsync()).Path;

        if (string.IsNullOrWhiteSpace(gamePath))
        {
            throw new InvalidOperationException("The Assetto Corsa folder is not configured. Reinstall the dashboard and point it at the game.");
        }

        var trackName = settings.TrackName!;
        var layout = string.IsNullOrWhiteSpace(settings.TrackLayout) ? null : settings.TrackLayout;

        var rollingStartLapCompensation = sessionType == SessionType.Race && RaceSessionLauncher.HasTrackAiSpline(gamePath, trackName, layout);

        var safetyCar = sessionType == SessionType.Race && rollingStartLapCompensation && settings.UseSafetyCar
            ? RaceSessionLauncher.ResolveSafetyCar(gamePath, (await (_configService?.GetAsync() ?? Task.FromResult<AssettoCorsaGameConfigModel?>(null)))?.SafetyCar?.Trim())
            : null;

        var (launchCars, playerStartPosition) = QuickRaceGrid.BuildLaunchCars(setup, human);

        var pitCount = await _trackService.GetPitCountAsync(trackName, layout);
        var carCount = launchCars.Count + (safetyCar is null ? 0 : 1);

        if (QuickRaceGrid.ValidatePitCapacity(carCount, pitCount) is { } capacityProblem)
        {
            throw new InvalidOperationException(capacityProblem);
        }

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

        // Plain in-memory objects: the builders read a race and its drivers, and nothing here is saved.
        var race = new Race
        {
            RaceName = "Quick Race",
            Status = RaceStatus.NotStarted,
            Season = null!,
            PointSetting = null!,
            Sessions = new List<Session>(),
            EnteredTeams = new List<Team>(),
            TrackName = trackName,
            TrackLayout = layout,
            NumberOfLaps = settings.NumberOfLaps,
            TimedRaceMinutes = RaceFormRules.NormalizeTimedRaceMinutes(settings.TimedRaceMinutes),
            QualifyingSessionMinutes = RaceFormRules.ClampQualifyingMinutes(settings.QualifyingMinutes),
            UseSafetyCar = settings.UseSafetyCar
        };

        var humanAsDriver = new Driver
        {
            DriverName = human.DriverName,
            CarDisplayName = human.CarDisplayName,
            Car = human.Car,
            Skin = human.Skin,
            Nationality = humanDriver?.Nationality,
            DriverStrength = humanDriver?.DriverStrength,
            DriverAgression = humanDriver?.DriverAgression,
            IsHuman = true
        };

        var entriesByIndex = QuickRaceGrid.Flatten(setup).Where(c => !c.Entry.IsHuman).Select(c => c.Entry).ToList();

        var opponents = entriesByIndex
            .Select(e => new Driver
            {
                DriverName = e.DriverName.Trim(),
                CarDisplayName = e.CarDisplayName,
                Car = e.Car.Trim(),
                Skin = e.Skin,
                Nationality = e.Nationality,
                DriverStrength = e.AiStrength ?? settings.AiDifficulty,
                DriverAgression = e.AiAggression ?? settings.AiAggression
            })
            .ToList();

        var raceIni = new RaceIniBuilder().Build(
            race,
            sessionType,
            humanAsDriver,
            opponents,
            settings.Weather,
            settings.SunAngle,
            playerStartPosition,
            settings.Penalties,
            rollingStartLapCompensation,
            (car, skin) => RaceSessionLauncher.ResolveSkin(gamePath, car, skin),
            safetyCar,
            quickRace: true);

        var classes = setup.ActiveClasses
            .Where(c => c.Entries.Count > 0)
            .Select(c => (c.Name, c.Color))
            .ToList();

        var manifest = SessionManifestBuilder.BuildQuickRace(classes, launchCars, sessionType);

        // Remembered before the game starts: the result can only be read against this grid.
        await _repository.SaveLaunchJsonAsync(QuickRaceJson.Serialize(new QuickRaceLaunchSnapshot
        {
            LaunchedUtc = DateTime.UtcNow,
            SessionType = sessionType,
            TrackName = trackName,
            TrackLayout = layout,
            Cars = launchCars.ToList()
        }));

        await _gameSessionLauncher.LaunchAsync(gamePath, executable, raceIni, manifest);

        _launchTracker.Record(new SessionLaunch(
            Guid.Empty,
            sessionType,
            sessionType == SessionType.Qualifying ? "Quick Race Qualifying" : "Quick Race",
            string.Empty,
            string.Empty,
            trackName,
            DateTimeOffset.UtcNow,
            IsQuickRace: true));
    }
}
