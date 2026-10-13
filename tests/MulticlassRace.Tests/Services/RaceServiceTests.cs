using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MulticlassRace.Models;
using MulticlassRace.Tests.Support;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.Services;

/// <summary>Pins the behaviour of the race back end: CRUD, launching, result import and preset export.</summary>
public class RaceServiceCrudTests : IDisposable
{
    private readonly RaceServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private RaceFormModel Form(Season season, string name = "  Monza  ", string? track = "monza", string? layout = null) => new()
    {
        RaceName = name,
        Country = " Italy ",
        SeasonId = season.SeasonId,
        PointSettingId = _fx.Context.PointSettings.First().SettingId,
        ClassSettingId = RaceServiceFixture.WecSettingId,
        TrackName = track,
        TrackLayout = layout,
        NumberOfLaps = 12,
        RaceDuration = 900
    };

    [Fact]
    public async Task Create_TrimsAndNormalisesAndDefaultsMinutes()
    {
        var season = _fx.AddSeason();
        var form = Form(season, layout: "  ");

        await _fx.Race.CreateRaceAsync(form);

        var race = await _fx.Context.Races.SingleAsync();
        Assert.Equal("Monza", race.RaceName);
        Assert.Equal("Italy", race.Country);
        Assert.Equal("monza", race.TrackName);
        Assert.Null(race.TrackLayout);
        Assert.Equal(RaceStatus.NotStarted, race.Status);
        Assert.Equal(20, race.PracticeSessionMinutes);
        Assert.Equal(20, race.QualifyingSessionMinutes);
        Assert.Equal(12, race.NumberOfLaps);
        Assert.Equal(900, race.RaceDuration);
    }

    [Theory]
    [InlineData(-5, 0, 0, 5)]
    [InlineData(500, 500, 90, 90)]
    [InlineData(0, 1, 0, 5)]
    public async Task Create_ClampsSessionMinutes(int practice, int qualifying, int expectedPractice, int expectedQualifying)
    {
        var season = _fx.AddSeason();
        var form = Form(season);
        form.PracticeSessionMinutes = practice;
        form.QualifyingSessionMinutes = qualifying;

        await _fx.Race.CreateRaceAsync(form);

        var race = await _fx.Context.Races.SingleAsync();
        Assert.Equal(expectedPractice, race.PracticeSessionMinutes);
        Assert.Equal(expectedQualifying, race.QualifyingSessionMinutes);
    }

    [Fact]
    public async Task Create_RejectsUnknownSeasonPointOrClassSetting()
    {
        var season = _fx.AddSeason();

        var noSeason = Form(season);
        noSeason.SeasonId = Guid.NewGuid();
        var noPoint = Form(season);
        noPoint.PointSettingId = Guid.NewGuid();
        var noClass = Form(season);
        noClass.ClassSettingId = Guid.NewGuid();

        Assert.Equal("Season not found.", (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Race.CreateRaceAsync(noSeason))).Message);
        Assert.Equal("A point setting must be selected.", (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Race.CreateRaceAsync(noPoint))).Message);
        Assert.Equal("A class setting with at least one class must be selected.", (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Race.CreateRaceAsync(noClass))).Message);
    }

    [Theory]
    [InlineData(null, null, "Select a track.")]
    [InlineData("spa", null, "Select a track layout.")]
    [InlineData("spa", "nope", "Select a valid layout for the chosen track.")]
    public async Task Create_ValidatesTrackAndLayout(string? track, string? layout, string message)
    {
        var season = _fx.AddSeason();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Race.CreateRaceAsync(Form(season, track: track, layout: layout)));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Create_AcceptsKnownLayoutCaseInsensitivelyAndBlankWhenTrackHasDefaultLayout()
    {
        var season = _fx.AddSeason();

        await _fx.Race.CreateRaceAsync(Form(season, "A", "spa", "GP"));
        await _fx.Race.CreateRaceAsync(Form(season, "B", "mugello", null));

        Assert.Equal(2, await _fx.Context.Races.CountAsync());
    }

    [Fact]
    public async Task Update_ChangesFieldsAndSettingsButIgnoresEmptySettingIds()
    {
        var season = _fx.AddSeason();
        var race = _fx.AddRace(season);
        var otherPoints = new PointSetting { SettingId = Guid.NewGuid(), SettingName = "Other", IsDefault = false, Config = new() { ["1"] = 5 } };
        _fx.Context.PointSettings.Add(otherPoints);
        await _fx.Context.SaveChangesAsync();

        var form = Form(season, "Renamed", "spa", "gp");
        form.RaceId = race.RaceId;
        form.PointSettingId = otherPoints.SettingId;
        form.ClassSettingId = Guid.Empty;

        await _fx.Race.UpdateRaceAsync(form);

        var updated = await _fx.Context.Races.SingleAsync();
        Assert.Equal("Renamed", updated.RaceName);
        Assert.Equal("spa", updated.TrackName);
        Assert.Equal("gp", updated.TrackLayout);
        Assert.Equal(otherPoints.SettingId, updated.PointSettingId);
        Assert.Equal(RaceServiceFixture.WecSettingId, updated.ClassSettingId);
    }

    [Fact]
    public async Task Update_UnknownRace_DoesNothing()
    {
        var season = _fx.AddSeason();
        var form = Form(season);
        form.RaceId = Guid.NewGuid();

        await _fx.Race.UpdateRaceAsync(form);

        Assert.Empty(_fx.Context.Races);
    }

    [Fact]
    public async Task GetRacesBySeason_MapsSortedByNameWithStandingCount()
    {
        var season = _fx.AddSeason();
        var zed = _fx.AddRace(season, "Zed");
        _fx.AddRace(season, "Alpha");
        _fx.AddRace(_fx.AddSeason("Other"), "Elsewhere");
        var team = _fx.AddTeam("T", "Hypercar", ("D", "c", "s", false));
        _fx.AddSession(zed, SessionType.Qualifying, (team.Drivers.First(), team, 1));

        var races = (await _fx.Race.GetRacesBySeasonAsync(season.SeasonId)).ToList();

        Assert.Equal(new[] { "Alpha", "Zed" }, races.Select(r => r.RaceName));
        Assert.Equal(new[] { 0, 1 }, races.Select(r => r.StandingCount));
        Assert.Equal("WEC", races[0].ClassSettingName);
        Assert.Equal("Standard", races[0].PointSettingName);
    }

    [Fact]
    public async Task GetRaceById_ReturnsNullWhenMissing()
    {
        Assert.Null(await _fx.Race.GetRaceByIdAsync(Guid.NewGuid()));

        var race = _fx.AddRace(_fx.AddSeason());
        Assert.Equal("Monza", (await _fx.Race.GetRaceByIdAsync(race.RaceId))!.RaceName);
    }

    [Fact]
    public async Task GetCandidateRaces_FiltersByTrackAndHidesFinishedExceptForRaceResults()
    {
        var season = _fx.AddSeason();
        _fx.AddRace(season, "Open", "monza");
        _fx.AddRace(season, "Done", "monza", status: RaceStatus.Finished);
        _fx.AddRace(season, "Elsewhere", "spa");

        var practice = (await _fx.Race.GetCandidateRacesAsync("monza", SessionType.Practice)).Select(r => r.RaceName);
        var race = (await _fx.Race.GetCandidateRacesAsync("monza", SessionType.Race)).Select(r => r.RaceName);
        var anyTrack = (await _fx.Race.GetCandidateRacesAsync(null, SessionType.Practice)).Select(r => r.RaceName);

        Assert.Equal(new[] { "Open" }, practice);
        Assert.Equal(new[] { "Done", "Open" }, race);
        Assert.Equal(new[] { "Elsewhere", "Open" }, anyTrack);
    }

    [Fact]
    public async Task Delete_RemovesRace()
    {
        var race = _fx.AddRace(_fx.AddSeason());

        await _fx.Race.DeleteRaceAsync(race.RaceId);

        Assert.Empty(_fx.Context.Races);
    }

    [Fact]
    public async Task SaveRaceTeams_AddsAndRemovesEnteredTeams()
    {
        var race = _fx.AddRace(_fx.AddSeason());
        var a = _fx.AddTeam("A");
        var b = _fx.AddTeam("B");

        await _fx.Race.SaveRaceTeamsAsync(race.RaceId, new[] { a.TeamId, b.TeamId });
        Assert.Equal(new[] { "A", "B" }, (await _fx.Race.GetRaceTeamsAsync(race.RaceId)).Select(t => t.TeamName));

        await _fx.Race.SaveRaceTeamsAsync(race.RaceId, new[] { b.TeamId });
        Assert.Equal(new[] { "B" }, (await _fx.Race.GetRaceTeamsAsync(race.RaceId)).Select(t => t.TeamName));
    }

    [Fact]
    public async Task GetRaceTeams_IncludesClassTags()
    {
        var race = _fx.AddRace(_fx.AddSeason());
        var team = _fx.AddTeam("A", "GT3");
        _fx.EnterTeams(race, team);

        var model = Assert.Single(await _fx.Race.GetRaceTeamsAsync(race.RaceId));

        var tag = Assert.Single(model.ClassTags);
        Assert.Equal("GT3", tag.ItemName);
        Assert.Equal("WEC", tag.SettingName);
    }

    [Fact]
    public async Task GetEnteredCarCount_CountsActiveDriversOnly()
    {
        var race = _fx.AddRace(_fx.AddSeason());
        var team = _fx.AddTeam("A", "Hypercar", ("One", "c", "s", false), ("Two", "c", "s", false));
        team.Drivers.Last().IsActive = false;
        await _fx.Context.SaveChangesAsync();
        _fx.EnterTeams(race, team);

        Assert.Equal(1, await _fx.Race.GetEnteredCarCountAsync(race.RaceId));
    }

    [Fact]
    public async Task GetRaceSessions_OrdersStandingsByClassPosition()
    {
        var race = _fx.AddRace(_fx.AddSeason());
        var team = _fx.AddTeam("A", "Hypercar", ("First", "c1", "s", false), ("Second", "c2", "s", false));
        var drivers = team.Drivers.ToList();
        _fx.AddSession(race, SessionType.Qualifying, (drivers[0], team, 2), (drivers[1], team, 1));

        var session = Assert.Single(await _fx.Race.GetRaceSessionsAsync(race.RaceId));

        Assert.Equal(SessionType.Qualifying, session.SessionType);
        Assert.Equal(new[] { 1, 2 }, session.DriverStandings.Select(s => s.ClassPosition));
    }
}

public class RaceServiceLaunchTests : IDisposable
{
    private readonly RaceServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private (Models.Race Race, Team Team) Setup(int laps = 10)
    {
        var race = _fx.AddRace(_fx.AddSeason(), laps: laps);
        var team = _fx.AddTeam("Ferrari", "Hypercar", ("Me", "ferrari_499p", "red", true), ("Bob", "ferrari_499p", "yellow", false));
        _fx.EnterTeams(race, team);
        return (race, team);
    }

    private Task Launch(Models.Race race, SessionType type = SessionType.Practice, bool penalties = true)
        => _fx.SessionLauncher.LaunchSessionAsync(race.RaceId, type, "1_heavy_fog", 48, penalties);

    private async Task<string> ExpectErrorAsync(Models.Race race, SessionType type = SessionType.Practice)
        => (await Assert.ThrowsAsync<InvalidOperationException>(() => Launch(race, type))).Message;

    [Fact]
    public async Task Launch_WritesIniForSessionAndRecordsLaunch()
    {
        var (race, _) = Setup();

        await Launch(race, SessionType.Practice, penalties: false);

        var launch = Assert.Single(_fx.Launcher.Launches);
        Assert.Equal(_fx.GameDir, launch.GamePath);
        Assert.Equal(Path.Combine(_fx.GameDir, "acs.exe"), launch.Executable);
        Assert.Contains("TRACK=monza", launch.RaceIni);
        Assert.Contains("SUN_ANGLE=48", launch.RaceIni);
        Assert.Contains("NAME=1_heavy_fog", launch.RaceIni);
        Assert.Contains("PENALTIES=0", launch.RaceIni);
        Assert.Contains("TYPE=1", launch.RaceIni);
        Assert.Contains("CARS=2", launch.RaceIni);
        Assert.Contains("MODEL=ferrari_499p", launch.RaceIni);

        var recorded = _fx.LaunchTracker.Current!;
        Assert.Equal(race.RaceId, recorded.RaceId);
        Assert.Equal(SessionType.Practice, recorded.SessionType);
        Assert.Equal("monza", recorded.TrackName);
    }

    [Fact]
    public async Task Launch_RefusesWhenThingsAreMissing()
    {
        var season = _fx.AddSeason();

        Assert.Equal("Race not found.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.SessionLauncher.LaunchSessionAsync(Guid.NewGuid(), SessionType.Practice, "w", 0))).Message);

        var noTrack = _fx.AddRace(season, "NoTrack", track: null);
        Assert.Equal("Select a track for this race before starting a session.", await ExpectErrorAsync(noTrack));

        var noLaps = _fx.AddRace(season, "NoLaps", laps: 0);
        Assert.Equal("Set the number of laps for this race before starting the session.", await ExpectErrorAsync(noLaps, SessionType.Race));

        var noPractice = _fx.AddRace(season, "NoPractice", practiceMinutes: 0);
        Assert.Equal("Practice is disabled for this race because its duration is set to 0 minutes.", await ExpectErrorAsync(noPractice));
    }

    [Fact]
    public async Task Launch_RefusesWhenMoreCarsThanPits()
    {
        var (race, _) = Setup();
        _fx.Tracks.PitCount = 1;

        Assert.Equal("The number of cars (2) is more than the number of pits (1). Please change the race track or layout.",
            await ExpectErrorAsync(race));
    }

    [Fact]
    public async Task Launch_PutsHumanFirstAndOrdersGridByClassThenTeamThenDriver()
    {
        var race = _fx.AddRace(_fx.AddSeason());
        var gt3 = _fx.AddTeam("Zeta", "GT3", ("Gt", "gt3car", "s", false));
        var hyper = _fx.AddTeam("Acme", "Hypercar", ("Zed", "hyper", "s", false), ("Amy", "hyper", "s", false), ("Me", "mine", "s", true));
        _fx.EnterTeams(race, gt3, hyper);

        await Launch(race);

        var ini = _fx.Launcher.Launches[0].RaceIni;
        Assert.Contains("CARS=4", ini);
        Assert.True(ini.IndexOf("DRIVER_NAME=Amy") < ini.IndexOf("DRIVER_NAME=Zed"));
        Assert.True(ini.IndexOf("DRIVER_NAME=Zed") < ini.IndexOf("DRIVER_NAME=Gt"));
        Assert.Contains("[CAR_0]", ini);
        Assert.Contains("DRIVER_NAME=Me", ini.Substring(ini.IndexOf("[CAR_0]"), 200));
    }

    [Fact]
    public async Task Launch_RaceNeedsQualifyingResults()
    {
        var (race, _) = Setup();

        Assert.Equal("A qualifying session with results is required before starting the race.",
            await ExpectErrorAsync(race, SessionType.Race));
    }

    [Fact]
    public async Task Launch_RaceGridFollowsQualifyingAndPlayerStartsWhereTheyQualified()
    {
        var (race, team) = Setup();
        var me = team.Drivers.Single(d => d.IsHuman);
        var bob = team.Drivers.Single(d => !d.IsHuman);
        _fx.AddSession(race, SessionType.Qualifying, (bob, team, 1), (me, team, 2));

        await Launch(race, SessionType.Race);

        var ini = _fx.Launcher.Launches[0].RaceIni;
        Assert.Contains("TYPE=3", ini);
        Assert.Contains("STARTING_POSITION=2", ini);
        Assert.Contains("RACE_LAPS=10", ini);
    }

    [Fact]
    public async Task Launch_RaceOnTrackWithAiSplineAddsFormationLapAndHeader()
    {
        var (race, team) = Setup();
        _fx.AddSession(race, SessionType.Qualifying, (team.Drivers.First(), team, 1));
        _fx.AddAiSpline("monza");

        await Launch(race, SessionType.Race);

        var ini = _fx.Launcher.Launches[0].RaceIni;
        Assert.Contains("RACE_LAPS=11", ini);
        Assert.Contains("__MCR_ROLLING_START_LAP_OFFSET=1", ini);
    }
}

public class RaceServiceImportTests : IDisposable
{
    private readonly RaceServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private static MemoryStream Json(string json) => new(System.Text.Encoding.UTF8.GetBytes(json));

    private (Models.Race Race, Team Hyper, Team Gt3) Setup()
    {
        var race = _fx.AddRace(_fx.AddSeason());
        var hyper = _fx.AddTeam("Ferrari", "Hypercar", ("Alonso", "c1", "s1", false), ("Me", "c2", "s2", true));
        var gt3 = _fx.AddTeam("Porsche", "GT3", ("Lieb", "c3", "s3", false));
        _fx.EnterTeams(race, hyper, gt3);
        return (race, hyper, gt3);
    }

    private async Task<List<DriverStanding>> StandingsAsync(Guid raceId, SessionType type)
    {
        var session = await _fx.Context.Set<Session>().Include(s => s.DriverStandings).SingleAsync(s => s.RaceId == raceId && s.SessionType == type);
        return session.DriverStandings.OrderBy(d => d.TeamClassName == "Hypercar" ? 0 : 1).ThenBy(d => d.Position).ToList();
    }

    [Fact]
    public async Task SessionResult_OrdersTimedThenUntimedWithinClassAndCountsUnknownAsSkipped()
    {
        var (race, _, _) = Setup();
        var json = """
        [
          {"driver":"Me","car":"c2","skin":"s2","bestLapTimeMs":"1:24.000"},
          {"driver":"Alonso","car":"c1","skin":"s1","bestLapTimeMs":"1:23.456"},
          {"driver":"Ghost","car":"zz","skin":"zz","bestLapTimeMs":"1:20.000"}
        ]
        """;

        var result = await _fx.Results.ImportSessionResultAsync(race.RaceId, SessionType.Qualifying, Json(json), "260401-120000.json");

        Assert.Equal(3, result.Imported);
        Assert.Equal(1, result.Skipped);
        var standings = await StandingsAsync(race.RaceId, SessionType.Qualifying);
        Assert.Equal(new[] { "Alonso", "Me", "Lieb" }, standings.Select(s => s.DriverName));
        Assert.Equal(new[] { 83456, 84000, 0 }, standings.Select(s => s.BestLapTimeMs));
        Assert.Equal(new[] { 1, 2, 1 }, standings.Select(s => s.ClassPosition));
        Assert.All(standings, s => Assert.Equal(0, s.Points));
        Assert.Equal(new DateTime(2026, 4, 1, 12, 0, 0), result.SessionDate);
        Assert.Equal(RaceStatus.InProgress, (await _fx.Context.Races.SingleAsync()).Status);
    }

    [Fact]
    public async Task SessionResult_UnparsableTimeKeepsDriverButUntimed()
    {
        var (race, _, _) = Setup();
        var json = """[{"driver":"Alonso","car":"c1","skin":"s1","bestLapTimeMs":"garbage"}]""";

        var result = await _fx.Results.ImportSessionResultAsync(race.RaceId, SessionType.Practice, Json(json), null);

        Assert.Equal(3, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.All(await StandingsAsync(race.RaceId, SessionType.Practice), s => Assert.Equal(0, s.BestLapTimeMs));
    }

    [Fact]
    public async Task SessionResult_ReplacesPreviousSessionOfSameTypeAndKeepsFinishedStatus()
    {
        var (race, _, _) = Setup();
        race.Status = RaceStatus.Finished;
        await _fx.Context.SaveChangesAsync();
        var json = """[{"driver":"Alonso","car":"c1","skin":"s1","bestLapTimeMs":"1:23.456"}]""";

        await _fx.Results.ImportSessionResultAsync(race.RaceId, SessionType.Qualifying, Json(json), null);
        await _fx.Results.ImportSessionResultAsync(race.RaceId, SessionType.Qualifying, Json(json), null);

        Assert.Equal(1, await _fx.Context.Set<Session>().CountAsync(s => s.SessionType == SessionType.Qualifying));
        Assert.Equal(RaceStatus.Finished, (await _fx.Context.Races.SingleAsync()).Status);
    }

    [Fact]
    public async Task SessionResult_Rejects()
    {
        var (race, _, _) = Setup();

        Assert.Equal("Only practice and qualifying results can be imported this way.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Results.ImportSessionResultAsync(race.RaceId, SessionType.Race, Json("[]"), null))).Message);
        Assert.Equal("The selected file is not a valid session result file.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Results.ImportSessionResultAsync(race.RaceId, SessionType.Practice, Json("{nope"), null))).Message);
        Assert.Equal("The selected file contains no result entries.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Results.ImportSessionResultAsync(race.RaceId, SessionType.Practice, Json("[]"), null))).Message);
        Assert.Equal("Race not found.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Results.ImportSessionResultAsync(Guid.NewGuid(), SessionType.Practice, Json("""[{"driver":"a","car":"b","skin":"c"}]"""), null))).Message);
    }

    private const string RaceJson = """
    {
      "track": "monza",
      "players": [
        {"name":"Alonso","car":"c1","skin":"s1"},
        {"name":"Me","car":"c2","skin":"s2"},
        {"name":"Lieb","car":"c3","skin":"s3"},
        {"name":"Stranger","car":"x","skin":"x"}
      ],
      "sessions": [
        {"type":1,"lapsCount":0},
        {"type":3,"lapsCount":10,"lapsTotal":[9,10,10,3],
         "bestLaps":[{"car":0,"time":91000,"lap":2},{"car":1,"time":90000,"lap":3},{"car":2,"time":95000,"lap":1}]}
      ]
    }
    """;

    private string WriteResult(string name, string json)
    {
        var path = Path.Combine(_fx.ResultsDir, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task RaceResult_RanksByLapsThenBestLapPerClassAndAwardsPoints()
    {
        var (race, _, _) = Setup();
        WriteResult("260401-130000.json", RaceJson);

        var result = await _fx.Results.ImportRaceResultAsync(race.RaceId, "260401-130000.json");

        Assert.Equal(3, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(SessionType.Race, result.SessionType);
        var standings = await StandingsAsync(race.RaceId, SessionType.Race);
        Assert.Equal(new[] { "Me", "Alonso", "Lieb" }, standings.Select(s => s.DriverName));
        Assert.Equal(new[] { 25, 18, 25 }, standings.Select(s => s.Points));
        Assert.Equal(new[] { 10, 9, 10 }, standings.Select(s => s.LapCount));
        Assert.Equal(new[] { "Hypercar", "Hypercar", "GT3" }, standings.Select(s => s.TeamClassName));
        Assert.Equal(RaceStatus.Finished, (await _fx.Context.Races.SingleAsync()).Status);
    }

    [Fact]
    public async Task RaceResult_FromPathUsesGivenNameAndFile()
    {
        var (race, _, _) = Setup();
        var path = Path.Combine(_fx.Root, "elsewhere.json");
        File.WriteAllText(path, RaceJson);

        var result = await _fx.Results.ImportRaceResultFromPathAsync(race.RaceId, path, null);

        Assert.Equal(3, result.Imported);
    }

    [Fact]
    public async Task RaceResult_Rejects()
    {
        var (race, _, _) = Setup();
        var invalid = WriteResult("260401-130001.json", "not json");
        var empty = WriteResult("260401-130002.json", """{"players":[],"sessions":[]}""");
        var noRace = WriteResult("260401-130003.json", """{"players":[],"sessions":[{"type":1}]}""");
        var valid = WriteResult("260401-130004.json", RaceJson);

        Assert.StartsWith("The selected file is not a valid Content Manager session file",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Results.ImportRaceResultFromPathAsync(race.RaceId, invalid, null))).Message);
        Assert.Equal("The selected file contains no sessions.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Results.ImportRaceResultFromPathAsync(race.RaceId, empty, null))).Message);
        Assert.Equal("The selected file contains no race session to import.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Results.ImportRaceResultFromPathAsync(race.RaceId, noRace, null))).Message);
        Assert.Equal("Race not found.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Results.ImportRaceResultFromPathAsync(Guid.NewGuid(), valid, null))).Message);
        Assert.StartsWith("Result file not found",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Results.ImportRaceResultFromPathAsync(race.RaceId, Path.Combine(_fx.Root, "missing.json"), "missing.json"))).Message);
    }

    [Fact]
    public async Task RaceResult_WithoutResultsFolder_Explains()
    {
        var (race, _, _) = Setup();
        Directory.Delete(_fx.ResultsDir);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Results.ImportRaceResultAsync(race.RaceId, "x.json"));

        Assert.StartsWith("There are no race results to import in", ex.Message);
    }

    [Fact]
    public async Task ResultFiles_ListsContentManagerAndLuaRaceFilesNewestFirst()
    {
        WriteResult("260401-130000.json", RaceJson);
        WriteResult("260403-100000.json", RaceJson);
        WriteResult("260404-100000.json", """{"track":"monza","sessions":[{"type":1}]}""");
        WriteResult("not-a-date.json", RaceJson);
        var luaRace = Path.Combine(_fx.LuaDir, "race");
        Directory.CreateDirectory(luaRace);
        File.WriteAllText(Path.Combine(luaRace, "260402-090000.json"), RaceJson);

        var files = (await _fx.Results.GetRaceResultFilesAsync()).ToList();

        Assert.Equal(new[] { "260403-100000.json", "260402-090000.json", "260401-130000.json" }, files.Select(f => f.FileName));
        Assert.Equal(new[] { false, true, false }, files.Select(f => f.IsLuaResult));
        Assert.All(files, f => Assert.Equal("monza", f.Track));
    }

    [Fact]
    public async Task Availability_FollowsFolders()
    {
        var both = await _fx.Results.GetResultImportAvailabilityAsync();
        Assert.True(both.CanImportResults);
        Assert.True(both.CanExportPresets);

        Directory.Delete(_fx.ResultsDir);
        Directory.Delete(_fx.PresetDir);

        var none = await _fx.Results.GetResultImportAvailabilityAsync();
        Assert.False(none.CanImportResults);
        Assert.False(none.CanExportPresets);

        Directory.CreateDirectory(Path.Combine(_fx.LuaDir, "race"));
        Assert.True((await _fx.Results.GetResultImportAvailabilityAsync()).CanImportResults);
    }
}

public class RaceServicePresetTests : IDisposable
{
    private readonly RaceServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private (Models.Race Race, Team Hyper, Team Gt3) Setup()
    {
        var race = _fx.AddRace(_fx.AddSeason(), "Monza 6h");
        var hyper = _fx.AddTeam("Ferrari", "Hypercar", ("Alonso", "c1", "s1", false), ("Me", "c2", "s2", true));
        var gt3 = _fx.AddTeam("Porsche", "GT3", ("Lieb", "c3", "s3", false));
        _fx.EnterTeams(race, hyper, gt3);
        return (race, hyper, gt3);
    }

    private static JsonElement Read(string path) => JsonDocument.Parse(File.ReadAllText(path)).RootElement;

    private static string[] Cars(JsonElement preset) => preset.GetProperty("CarIds").EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task GridPreset_AllCarsOrderedByClassAndNamedAfterSession()
    {
        var (race, _, _) = Setup();

        var result = await _fx.Presets.ExportGridPresetAsync(race.RaceId, SessionType.Race, humanClassOnly: false, "S1", "WEC");

        Assert.Equal(Path.Combine(_fx.PresetDir, "Race-Monza 6h-S1-WEC.cmpreset"), result.FilePath);
        Assert.Equal(3, result.CarCount);
        Assert.Equal(new[] { "c1", "c2", "c3" }, Cars(Read(result.FilePath)));
    }

    [Theory]
    [InlineData(SessionType.Practice)]
    [InlineData(SessionType.Qualifying)]
    public async Task GridPreset_LeavesHumanOutOfPracticeAndQualifying(SessionType type)
    {
        var (race, _, _) = Setup();

        var result = await _fx.Presets.ExportGridPresetAsync(race.RaceId, type, false, "S1", "WEC");

        Assert.Equal(new[] { "c1", "c3" }, Cars(Read(result.FilePath)));
    }

    [Fact]
    public async Task GridPreset_HumanClassOnlyKeepsJustThatClass()
    {
        var (race, _, _) = Setup();

        var result = await _fx.Presets.ExportGridPresetAsync(race.RaceId, SessionType.Race, true, "S1", "WEC");

        Assert.Equal(new[] { "c1", "c2" }, Cars(Read(result.FilePath)));
    }

    [Fact]
    public async Task GridPreset_Rejects()
    {
        var race = _fx.AddRace(_fx.AddSeason());
        var noHuman = _fx.AddTeam("A", "Hypercar", ("X", "c", "s", false));

        Assert.Equal("Race not found.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Presets.ExportGridPresetAsync(Guid.NewGuid(), SessionType.Race, false, "s", "c"))).Message);
        Assert.Equal("No cars available to export from the entered teams.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Presets.ExportGridPresetAsync(race.RaceId, SessionType.Race, false, "s", "c"))).Message);

        _fx.EnterTeams(race, noHuman);
        Assert.Equal("No human drivers found in the entered teams.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Presets.ExportGridPresetAsync(race.RaceId, SessionType.Race, true, "s", "c"))).Message);

        Directory.Delete(_fx.PresetDir);
        Assert.StartsWith("Content Manager is not installed",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Presets.ExportGridPresetAsync(race.RaceId, SessionType.Race, false, "s", "c"))).Message);
    }

    [Fact]
    public async Task GridPreset_FileNameHasInvalidCharactersReplaced()
    {
        var race = _fx.AddRace(_fx.AddSeason(), "A:B/C");
        _fx.EnterTeams(race, _fx.AddTeam("T", "Hypercar", ("X", "c", "s", false)));

        var result = await _fx.Presets.ExportGridPresetAsync(race.RaceId, SessionType.Practice, false, "S", "W");

        Assert.Equal("Practice-A_B_C-S-W.cmpreset", Path.GetFileName(result.FilePath));
    }

    [Fact]
    public async Task RacePreset_HumanSetsStartingPositionFromQualifyingAndOthersFollowQualifyingOrder()
    {
        var (race, hyper, gt3) = Setup();
        var alonso = hyper.Drivers.Single(d => d.DriverName == "Alonso");
        var me = hyper.Drivers.Single(d => d.IsHuman);
        _fx.AddSession(race, SessionType.Qualifying, (me, hyper, 2), (alonso, hyper, 1));

        var result = await _fx.Presets.ExportRaceGridPresetAsync(race.RaceId, "S1", "WEC");

        var preset = Read(result.FilePath);
        Assert.Equal("Race-Monza 6h-S1-WEC.cmpreset", Path.GetFileName(result.FilePath));
        Assert.Equal(new[] { "c1", "c3" }, Cars(preset));
        Assert.Equal(2, preset.GetProperty("StartingPosition").GetInt32());
        Assert.Equal(2, result.CarCount);
    }

    [Fact]
    public async Task RacePreset_WithoutQualifyingStartsFirst()
    {
        var (race, _, _) = Setup();

        var result = await _fx.Presets.ExportRaceGridPresetAsync(race.RaceId, "S1", "WEC");

        Assert.Equal(1, Read(result.FilePath).GetProperty("StartingPosition").GetInt32());
    }
}
