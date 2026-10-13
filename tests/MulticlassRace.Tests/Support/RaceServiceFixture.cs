using MulticlassRace.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Repositories;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Races;

namespace MulticlassRace.Tests.Support;

public sealed class FakeGameSessionLauncher : IGameSessionLauncher
{
    public List<(string GamePath, string Executable, string RaceIni)> Launches { get; } = new();

    public Task LaunchAsync(string gamePath, string executablePath, string raceIni)
    {
        Launches.Add((gamePath, executablePath, raceIni));
        return Task.CompletedTask;
    }
}

/// <summary>
/// The real race services, repositories and path resolver on in-memory SQLite, with a temporary game
/// folder, Content Manager folders and Lua result folder, so the race back end can be exercised end to end.
/// </summary>
public sealed class RaceServiceFixture : IDisposable
{
    public static readonly Guid WecSettingId = new(SeedIds.DefaultClassSettingId);

    public RaceServiceFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "mcr-tests", Guid.NewGuid().ToString("N"));
        GameDir = Path.Combine(Root, "game");
        PresetDir = Path.Combine(Root, "presets");
        ResultsDir = Path.Combine(Root, "cm-results");
        LuaDir = Path.Combine(Root, "lua");

        Directory.CreateDirectory(GameDir);
        File.WriteAllText(Path.Combine(GameDir, "acs.exe"), "");
        Directory.CreateDirectory(PresetDir);
        Directory.CreateDirectory(ResultsDir);

        Db = new TestDb();
        Context = Db.CreateContext();

        Context.AssettoCorsaGameConfigs.Add(new AssettoCorsaGameConfig
        {
            AssettoCorsaGameConfigId = Guid.NewGuid(),
            GamePath = GameDir,
            PresetPath = PresetDir,
            RaceResultsPath = ResultsDir,
            LuaResultPath = LuaDir
        });
        Context.SaveChanges();

        var services = new ServiceCollection();
        services.AddScoped(_ => Db.CreateContext());
        services.AddScoped<IAssettoCorsaGameConfigRepository, AssettoCorsaGameConfigRepository>();
        var resolver = new AssettoCorsaPathResolver(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AssettoCorsaPathResolver>.Instance);

        Tracks = new FakeTrackService();
        Tracks.Tracks.Add(new MulticlassRace.ViewModels.TrackOptionModel("mugello", "Mugello"));
        Tracks.Layouts["mugello"] = new()
        {
            new MulticlassRace.ViewModels.TrackLayoutOptionModel("", "Default"),
            new MulticlassRace.ViewModels.TrackLayoutOptionModel("club", "Club")
        };

        Launcher = new FakeGameSessionLauncher();
        LaunchTracker = new SessionLaunchTracker();

        var raceRepository = new RaceRepository(Context);
        var configRepository = new AssettoCorsaGameConfigRepository(Context);
        var pointRepository = new PointSettingRepository(Context);

        Race = new RaceService(
            raceRepository,
            new SeasonRepository(Context),
            pointRepository,
            new ClassSettingRepository(Context),
            Tracks);
        SessionLauncher = new RaceSessionLauncher(raceRepository, Tracks, Launcher, LaunchTracker, resolver);
        Results = new RaceResultService(raceRepository, configRepository, pointRepository);
        Presets = new GridPresetService(raceRepository, configRepository);
    }

    public string Root { get; }

    public string GameDir { get; }

    public string PresetDir { get; }

    public string ResultsDir { get; }

    public string LuaDir { get; }

    public TestDb Db { get; }

    public AppDbContext Context { get; }

    public FakeTrackService Tracks { get; }

    public FakeGameSessionLauncher Launcher { get; }

    public SessionLaunchTracker LaunchTracker { get; }

    public IRaceService Race { get; }

    public IRaceSessionLauncher SessionLauncher { get; }

    public IRaceResultService Results { get; }

    public IGridPresetService Presets { get; }

    public Season AddSeason(string name = "Season 1")
    {
        var championship = new Championship { ChampionshipId = Guid.NewGuid(), ChampionshipName = "WEC" };
        var season = new Season { SeasonId = Guid.NewGuid(), SeasonName = name, Championship = championship };
        Context.Seasons.Add(season);
        Context.SaveChanges();
        return season;
    }

    public Models.Race AddRace(
        Season season,
        string name = "Monza",
        string? track = "monza",
        int? laps = 10,
        RaceStatus status = RaceStatus.NotStarted,
        int? practiceMinutes = 20)
    {
        var race = new Models.Race
        {
            RaceId = Guid.NewGuid(),
            RaceName = name,
            TrackName = track,
            NumberOfLaps = laps,
            Status = status,
            PracticeSessionMinutes = practiceMinutes,
            QualifyingSessionMinutes = 20,
            PointSettingId = Context.PointSettings.First().SettingId,
            PointSetting = Context.PointSettings.First(),
            ClassSettingId = WecSettingId,
            ClassSetting = Context.ClassSettings.Include(c => c.Items).First(c => c.ClassSettingId == WecSettingId),
            Season = season,
            Sessions = new List<Session>(),
            EnteredTeams = new List<Team>()
        };

        Context.Races.Add(race);
        Context.SaveChanges();
        return race;
    }

    public Team AddTeam(string name, string className = "Hypercar", params (string Name, string Car, string Skin, bool Human)[] drivers)
    {
        var item = Context.ClassSettingItems.First(i => i.ClassSettingId == WecSettingId && i.Name == className);
        var team = new Team { TeamId = Guid.NewGuid(), TeamName = name, ClassSettingItems = { item } };

        foreach (var d in drivers)
        {
            team.Drivers.Add(new Driver
            {
                DriverId = Guid.NewGuid(),
                DriverName = d.Name,
                Car = d.Car,
                Skin = d.Skin,
                CarDisplayName = d.Car,
                IsHuman = d.Human,
                Nationality = "Italy"
            });
        }

        Context.Teams.Add(team);
        Context.SaveChanges();
        return team;
    }

    public void EnterTeams(Models.Race race, params Team[] teams)
    {
        var tracked = Context.Races.Include(r => r.EnteredTeams).First(r => r.RaceId == race.RaceId);

        foreach (var team in teams)
        {
            tracked.EnteredTeams.Add(team);
        }

        Context.SaveChanges();
    }

    public void AddSession(Models.Race race, SessionType type, params (Driver Driver, Team Team, int ClassPosition)[] standings)
    {
        var session = new Session
        {
            SessionId = Guid.NewGuid(),
            SessionType = type,
            SessionDate = DateTime.Now,
            RaceId = race.RaceId,
            Race = race,
            DriverStandings = new List<DriverStanding>()
        };

        foreach (var s in standings)
        {
            session.DriverStandings.Add(new DriverStanding
            {
                DriverStandingId = Guid.NewGuid(),
                Driver = s.Driver,
                DriverName = s.Driver.DriverName,
                OriginalTeamId = s.Team.TeamId,
                TeamName = s.Team.TeamName,
                TeamClassName = "Hypercar",
                Race = race,
                Position = s.ClassPosition,
                ClassPosition = s.ClassPosition
            });
        }

        Context.Set<Session>().Add(session);
        Context.SaveChanges();
    }

    public void AddAiSpline(string track, string? layout = null)
    {
        var directory = layout is null
            ? Path.Combine(GameDir, "content", "tracks", track, "ai")
            : Path.Combine(GameDir, "content", "tracks", track, layout, "ai");

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "fast_lane.ai"), "");
    }

    public void Dispose()
    {
        Context.Dispose();
        Db.Dispose();

        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
