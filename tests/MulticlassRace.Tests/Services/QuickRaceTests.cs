using System.Text.Json;
using MulticlassRace.Builders;
using MulticlassRace.Models;
using MulticlassRace.Repositories;
using MulticlassRace.Services.QuickRace;
using MulticlassRace.Services.Races;
using MulticlassRace.Services.Races.ImportModels;
using MulticlassRace.Services.Results;
using MulticlassRace.Tests.Support;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.Services;

public class QuickRaceGridTests
{
    private static QuickRaceEntryModel Entry(string name, bool human = false, string car = "car_a")
        => new() { DriverName = name, Car = car, Skin = "s", CarDisplayName = car, IsHuman = human };

    private static QuickRaceSetupModel Setup(QuickRaceGridMode mode = QuickRaceGridMode.Custom)
    {
        var gt3 = new QuickRaceClassModel { Name = "GT3", Entries = { Entry("Me", human: true), Entry("A"), Entry("B"), Entry("C") } };
        var gt4 = new QuickRaceClassModel { Name = "GT4", Entries = { Entry("D"), Entry("E") } };

        var setup = new QuickRaceSetupModel { Mode = mode };
        var list = mode == QuickRaceGridMode.Custom ? setup.CustomClasses : setup.SystemClasses;
        list.Add(gt3);
        list.Add(gt4);

        return setup;
    }

    private static string[] Names(QuickRaceClassModel column) => column.Entries.Select(e => e.DriverName).ToArray();

    [Fact]
    public void MoveEntry_DroppedOnARowFurtherUp_LandsInFrontOfIt()
    {
        var setup = Setup();
        var column = setup.CustomClasses[0];

        Assert.True(QuickRaceGrid.MoveEntry(setup, column.Entries[3].EntryId, column.ClassId, column.Entries[1].EntryId));

        Assert.Equal(new[] { "Me", "C", "A", "B" }, Names(column));
    }

    [Fact]
    public void MoveEntry_DroppedOnARowFurtherDown_TakesItsPlace()
    {
        var setup = Setup();
        var column = setup.CustomClasses[0];

        Assert.True(QuickRaceGrid.MoveEntry(setup, column.Entries[1].EntryId, column.ClassId, column.Entries[3].EntryId));

        Assert.Equal(new[] { "Me", "B", "C", "A" }, Names(column));
    }

    [Fact]
    public void MoveEntry_DroppedOnAColumn_GoesToItsBottom()
    {
        var setup = Setup();
        var from = setup.CustomClasses[0];
        var to = setup.CustomClasses[1];

        Assert.True(QuickRaceGrid.MoveEntry(setup, from.Entries[1].EntryId, to.ClassId, null));

        Assert.Equal(new[] { "D", "E", "A" }, Names(to));
        Assert.Equal(new[] { "Me", "B", "C" }, Names(from));
    }

    [Fact]
    public void MoveEntry_InAClassSystemGrid_OnlyTheHumanChangesClass()
    {
        var setup = Setup(QuickRaceGridMode.ExistingClasses);
        var from = setup.SystemClasses[0];
        var to = setup.SystemClasses[1];

        Assert.False(QuickRaceGrid.MoveEntry(setup, from.Entries[1].EntryId, to.ClassId, null));
        Assert.True(QuickRaceGrid.MoveEntry(setup, from.Entries[0].EntryId, to.ClassId, null));
        Assert.Contains(to.Entries, e => e.IsHuman);
    }

    [Fact]
    public void MoveClass_ChangesOnlyTheColumnOrder()
    {
        var setup = Setup();
        var gt3 = setup.CustomClasses[0];
        var gt4 = setup.CustomClasses[1];

        Assert.True(QuickRaceGrid.MoveClass(setup, gt4.ClassId, gt3.ClassId));

        Assert.Equal(new[] { "GT4", "GT3" }, setup.CustomClasses.Select(c => c.Name).ToArray());
        Assert.Equal(new[] { "Me", "A", "B", "C" }, Names(gt3));
    }

    [Fact]
    public void RemoveEntry_NeverRemovesTheHuman()
    {
        var setup = Setup();
        var human = setup.CustomClasses[0].Entries[0];
        var other = setup.CustomClasses[0].Entries[1];

        Assert.False(QuickRaceGrid.RemoveEntry(setup, human.EntryId));
        Assert.True(QuickRaceGrid.RemoveEntry(setup, other.EntryId));
        Assert.Equal(3, setup.CustomClasses[0].Entries.Count);
    }

    [Fact]
    public void RemoveClass_KeepsTheHumanOnTheGrid()
    {
        var setup = Setup();

        Assert.True(QuickRaceGrid.RemoveClass(setup, setup.CustomClasses[0].ClassId));

        Assert.Single(setup.CustomClasses);
        Assert.Contains(setup.CustomClasses[0].Entries, e => e.IsHuman);
    }

    [Fact]
    public void Normalize_AddsAColumnAndTheHumanToAnEmptyGrid()
    {
        var setup = new QuickRaceSetupModel();

        QuickRaceGrid.Normalize(setup, "Dana");

        var human = Assert.Single(setup.CustomClasses.SelectMany(c => c.Entries));
        Assert.True(human.IsHuman);
        Assert.Equal("Dana", human.DriverName);
    }

    [Fact]
    public void BuildLaunchCars_PutsTheHumanAtIndexZeroAndReportsTheStartPosition()
    {
        var setup = Setup();
        QuickRaceGrid.MoveEntry(setup, setup.CustomClasses[0].Entries[0].EntryId, setup.CustomClasses[0].ClassId, setup.CustomClasses[0].Entries[2].EntryId);
        var human = new QuickRaceHuman("Me", "car_me", "skin_me", "My Car");

        var (cars, position) = QuickRaceGrid.BuildLaunchCars(setup, human);

        Assert.Equal(3, position);
        Assert.Equal(0, cars[0].Index);
        Assert.True(cars[0].IsHuman);
        Assert.Equal("car_me", cars[0].Car);
        Assert.Equal(new[] { "A", "B", "C", "D", "E" }, cars.Skip(1).Select(c => c.DriverName).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, cars.Skip(1).Select(c => c.Index).ToArray());
        Assert.Equal("GT4", cars[^1].ClassName);
    }

    [Fact]
    public void ValidateGrid_NeedsACarAnOpponentAndDistinctClassNames()
    {
        var human = new QuickRaceHuman("Me", "car_me", "s", "Car");

        Assert.Equal("Select a car for the human driver.", QuickRaceGrid.ValidateGrid(Setup(), new QuickRaceHuman("Me", "", "", "")));
        Assert.Null(QuickRaceGrid.ValidateGrid(Setup(), human));

        var alone = new QuickRaceSetupModel();
        QuickRaceGrid.Normalize(alone, "Me");
        Assert.Equal("Add at least one opponent to the grid.", QuickRaceGrid.ValidateGrid(alone, human));

        var duplicate = Setup();
        duplicate.CustomClasses[1].Name = "gt3";
        Assert.Equal("Two classes share the same name.", QuickRaceGrid.ValidateGrid(duplicate, human));
    }

    [Fact]
    public void ValidateSettings_ChecksTrackLayoutAndLength()
    {
        var layouts = new[] { new TrackLayoutOptionModel("gp", "GP") };

        Assert.Equal("Select a track.", QuickRaceGrid.ValidateSettings(new QuickRaceSettingsModel(), layouts));
        Assert.Equal("Select a track layout.", QuickRaceGrid.ValidateSettings(new QuickRaceSettingsModel { TrackName = "spa" }, layouts));
        Assert.Equal("Set the number of laps.", QuickRaceGrid.ValidateSettings(new QuickRaceSettingsModel { TrackName = "spa", TrackLayout = "gp", NumberOfLaps = 0 }, layouts));
        Assert.Null(QuickRaceGrid.ValidateSettings(new QuickRaceSettingsModel { TrackName = "spa", TrackLayout = "gp", NumberOfLaps = 5 }, layouts));
        Assert.Null(QuickRaceGrid.ValidateSettings(new QuickRaceSettingsModel { TrackName = "spa", TrackLayout = "gp", NumberOfLaps = null, TimedRaceMinutes = 30 }, layouts));
    }

    [Fact]
    public void SyncSystemClasses_FollowsTheClassSettingAndKeepsTheColumnOrder()
    {
        var gt3 = new ClassSettingItemModel { ClassSettingItemId = Guid.NewGuid(), Name = "GT3", Order = 0, Color = "#111111" };
        var gt4 = new ClassSettingItemModel { ClassSettingItemId = Guid.NewGuid(), Name = "GT4", Order = 1, Color = "#222222" };
        var setting = new ClassSettingModel { ClassSettingId = Guid.NewGuid(), ClassSettingName = "S", Items = { gt3, gt4 } };
        var setup = new QuickRaceSetupModel { Mode = QuickRaceGridMode.ExistingClasses };

        QuickRaceGrid.SyncSystemClasses(setup, setting);
        QuickRaceGrid.MoveClass(setup, gt4.ClassSettingItemId, gt3.ClassSettingItemId);
        gt3.Name = "GT3 Pro";
        QuickRaceGrid.SyncSystemClasses(setup, setting);

        Assert.Equal(new[] { "GT4", "GT3 Pro" }, setup.SystemClasses.Select(c => c.Name).ToArray());
    }
}

public class QuickRaceFileTests
{
    [Theory]
    [InlineData("261117-203015.json", true, false)]
    [InlineData("quick-race-261117-203015.json", true, true)]
    [InlineData("QUICK-RACE-261117-203015.json", true, true)]
    [InlineData("quick-race-nonsense.json", false, true)]
    [InlineData("notes.json", false, false)]
    public void TryParseFileName_ReadsTheDateWithOrWithoutThePrefix(string name, bool parsed, bool quick)
    {
        Assert.Equal(parsed, LuaResultPaths.TryParseFileName(name, out var date, out var isQuick));
        Assert.Equal(quick, isQuick);

        if (parsed)
        {
            Assert.Equal(new DateTime(2026, 11, 17, 20, 30, 15), date);
        }
    }

    [Fact]
    public void ChampionshipFileNameParsing_StillRejectsQuickRaceFiles()
    {
        Assert.False(ResultFileNames.TryParseSessionDate("quick-race-261117-203015.json", out _));
    }

    [Fact]
    public void RaceIni_MarksAQuickRaceOnlyWhenAsked()
    {
        var race = new Race { RaceName = "Quick Race", Status = RaceStatus.NotStarted, Season = null!, PointSetting = null!, Sessions = new List<Session>(), EnteredTeams = new List<Team>(), TrackName = "spa", NumberOfLaps = 5 };
        var human = new Driver { DriverName = "Me", CarDisplayName = "Me", Car = "car_a", Skin = "s", IsHuman = true };
        var ai = new Driver { DriverName = "Bot", CarDisplayName = "Bot", Car = "car_b", Skin = "s" };

        var quick = new RaceIniBuilder().Build(race, SessionType.Race, human, new[] { ai }, "3_clear", -16, quickRace: true);
        var normal = new RaceIniBuilder().Build(race, SessionType.Race, human, new[] { ai }, "3_clear", -16);

        Assert.Contains("__MCR_QUICK_RACE=1", quick);
        Assert.DoesNotContain("__MCR_QUICK_RACE", normal);
    }

    [Fact]
    public void QuickRaceManifest_ListsTheGridColumnsAsClasses()
    {
        var cars = new List<QuickRaceLaunchCar>
        {
            new() { Index = 0, DriverName = "Me", Car = "car_a", ClassName = "GT3", IsHuman = true },
            new() { Index = 1, DriverName = "Bot", Car = "car_b", ClassName = "GT4", TeamName = "Team" }
        };

        using var doc = JsonDocument.Parse(SessionManifestBuilder.BuildQuickRace(new[] { ("GT3", "#111"), ("GT4", "#222") }, cars));
        var root = doc.RootElement;

        Assert.True(root.GetProperty("quickRace").GetBoolean());
        Assert.Equal("GT4", root.GetProperty("classes")[1].GetProperty("name").GetString());
        Assert.Equal(1, root.GetProperty("classes")[1].GetProperty("order").GetInt32());
        Assert.Equal("car_b", root.GetProperty("cars")[1].GetProperty("carId").GetString());
        Assert.Equal("GT4", root.GetProperty("cars")[1].GetProperty("class").GetString());
    }
}

public class RandomNameServiceTests
{
    [Fact]
    public void Generate_CombinesAFirstAndALastName_AvoidingTakenOnes()
    {
        var service = new RandomNameService(new[] { "Ann", "Bob" }, new[] { "Lee" });
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ann Lee" };

        for (var i = 0; i < 30; i++)
        {
            Assert.Equal("Bob Lee", service.Generate(taken));
        }
    }

    [Fact]
    public void TheShippedNameList_HasCommonNamesWithoutDuplicates()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "wwwroot", RandomNameService.DataFile)))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "wwwroot", RandomNameService.DataFile)));
        var first = document.RootElement.GetProperty("firstNames").EnumerateArray().Select(n => n.GetString()!).ToList();
        var last = document.RootElement.GetProperty("lastNames").EnumerateArray().Select(n => n.GetString()!).ToList();

        Assert.True(first.Count >= 100);
        Assert.True(last.Count >= 100);
        Assert.Equal(first.Count, first.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(last.Count, last.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("James", first);
        Assert.Contains("Smith", last);
    }
}

public class QuickRaceServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mcr-quick-" + Guid.NewGuid().ToString("N"));

    public QuickRaceServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _db.Dispose();

        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private QuickRaceService CreateService(RaceSessionChangeNotifier? notifier = null)
    {
        var context = _db.CreateContext();

        return new QuickRaceService(
            new QuickRaceRepository(context),
            new ImportedLuaResultRepository(context),
            notifier ?? new RaceSessionChangeNotifier());
    }

    /// <summary>A result as the Lua app writes it: car 0 is the human, car 1 and 2 are AI, finishing 2, 0, 1.</summary>
    private string WriteResult(string fileName)
    {
        var path = Path.Combine(_folder, fileName);
        File.WriteAllText(path, """
        {
          "track": "spa",
          "numberOfSessions": 1,
          "players": [
            { "name": "Me", "car": "car_a", "skin": "s0" },
            { "name": "Bot One", "car": "car_b", "skin": "s1" },
            { "name": "Bot Two", "car": "car_b", "skin": "s2" },
            { "name": "Safety Car", "car": "car_sc", "skin": "s" }
          ],
          "sessions": [
            { "type": 3, "lapsCount": 5, "lapsTotal": [5, 5, 5, 0],
              "bestLaps": [ { "car": 0, "lap": 2, "time": 95000 }, { "car": 2, "lap": 3, "time": 94000 } ],
              "raceResult": [2, 0, 1] }
          ]
        }
        """);

        return path;
    }

    private async Task SeedLaunchAsync()
    {
        var repository = new QuickRaceRepository(_db.CreateContext());
        var snapshot = new QuickRaceLaunchSnapshot
        {
            TrackName = "spa",
            Cars =
            {
                new QuickRaceLaunchCar { Index = 0, DriverName = "Me", Car = "car_a", Skin = "s0", CarDisplayName = "Car A", ClassName = "GT3", ClassColor = "#111", IsHuman = true },
                new QuickRaceLaunchCar { Index = 1, DriverName = "Bot One", Car = "car_b", Skin = "s1", CarDisplayName = "Car B", ClassName = "GT4", ClassColor = "#222" },
                new QuickRaceLaunchCar { Index = 2, DriverName = "Bot Two", Car = "car_b", Skin = "s2", CarDisplayName = "Car B", ClassName = "GT4", ClassColor = "#222" }
            }
        };

        await repository.SaveLaunchJsonAsync(JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public async Task SetupRoundTripsThroughTheDatabase()
    {
        var service = CreateService();
        var setup = new QuickRaceSetupModel { Mode = QuickRaceGridMode.ExistingClasses, ClassSettingId = Guid.NewGuid() };
        setup.Settings.TrackName = "spa";
        setup.Settings.TimedRaceMinutes = 45;
        setup.CustomClasses.Add(new QuickRaceClassModel { Name = "Mine", Entries = { new QuickRaceEntryModel { DriverName = "A", Car = "c" } } });

        await service.SaveSetupAsync(setup);
        var loaded = await CreateService().GetSetupAsync();

        Assert.Equal(QuickRaceGridMode.ExistingClasses, loaded.Mode);
        Assert.Equal(setup.ClassSettingId, loaded.ClassSettingId);
        Assert.Equal(45, loaded.Settings.TimedRaceMinutes);
        Assert.Equal("A", loaded.CustomClasses[0].Entries[0].DriverName);
    }

    [Fact]
    public async Task GetSetup_WithNothingSaved_ReturnsAnEmptySetup()
    {
        var setup = await CreateService().GetSetupAsync();

        Assert.Empty(setup.CustomClasses);
        Assert.Equal(QuickRaceGridMode.Custom, setup.Mode);
    }

    [Fact]
    public async Task Import_RanksCarsReadsClassesFromTheLaunchedGridAndKeepsHistory()
    {
        await SeedLaunchAsync();
        var notified = new List<Guid>();
        var notifier = new RaceSessionChangeNotifier();
        notifier.QuickRaceImported += notified.Add;
        var service = CreateService(notifier);

        var result = await service.ImportResultFromPathAsync(WriteResult("quick-race-261117-203015.json"), "quick-race-261117-203015.json");

        Assert.Equal(new[] { "Bot Two", "Me", "Bot One" }, result.Rows.Select(r => r.DriverName).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, result.Rows.Select(r => r.Position).ToArray());
        Assert.Equal(new[] { 1, 1, 2 }, result.Rows.Select(r => r.ClassPosition).ToArray());
        Assert.Equal(2, result.HumanPosition);
        Assert.Equal(1, result.HumanClassPosition);
        Assert.Equal("GT3", result.HumanClassName);
        Assert.DoesNotContain(result.Rows, r => r.DriverName == "Safety Car");
        Assert.Equal(new DateTime(2026, 11, 17, 20, 30, 15), result.RaceDate);
        Assert.Equal(new[] { result.ResultId }, notified);

        var history = await CreateService().GetHistoryAsync();
        Assert.Equal(2, history.Single().HumanPosition);

        var stored = await CreateService().GetResultAsync(result.ResultId);
        Assert.Equal(3, stored!.Rows.Count);
    }

    [Fact]
    public async Task Import_OfTheSameFileTwice_IsRejected()
    {
        await SeedLaunchAsync();
        var service = CreateService();
        var path = WriteResult("quick-race-261117-203015.json");

        await service.ImportResultFromPathAsync(path, null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().ImportResultFromPathAsync(path, null));
        Assert.Contains("already", error.Message);
    }

    [Fact]
    public async Task Import_OfAFileWithoutARace_FailsWithAReadableMessage()
    {
        var path = Path.Combine(_folder, "quick-race-261117-203016.json");
        File.WriteAllText(path, """{ "track": "spa", "players": [], "sessions": [ { "type": 1 } ] }""");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().ImportResultFromPathAsync(path, null));

        Assert.Contains("no race session", error.Message);
    }

    [Fact]
    public async Task DeleteResult_RemovesItFromTheHistory()
    {
        await SeedLaunchAsync();
        var service = CreateService();
        var result = await service.ImportResultFromPathAsync(WriteResult("quick-race-261117-203015.json"), null);

        await CreateService().DeleteResultAsync(result.ResultId);

        Assert.Empty(await CreateService().GetHistoryAsync());
    }
}

public class LuaResultImportStateTests
{
    [Fact]
    public void MarkHandled_ReportsWhetherThePathWasNew()
    {
        var state = new LuaResultImportState();

        Assert.True(state.MarkHandled(@"C:\r\race\a.json"));
        Assert.False(state.MarkHandled(@"c:\R\race\A.json"));
        Assert.True(state.IsKnown(@"C:\r\race\a.json"));
    }

    [Fact]
    public void Report_ReachesEverySubscriberEvenWhenOneThrows()
    {
        var state = new LuaResultImportState();
        var seen = new List<string>();
        state.OutcomeReported += _ => throw new InvalidOperationException("circuit gone");
        state.OutcomeReported += outcome => seen.Add(outcome.Message);

        state.Report(new LuaResultImportOutcome(false, "Could not find the session result. Please import it manually.", true));

        Assert.Equal(new[] { "Could not find the session result. Please import it manually." }, seen);
    }
}

public class QuickRaceQualifyingTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mcr-quali-" + Guid.NewGuid().ToString("N"));

    public QuickRaceQualifyingTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _db.Dispose();

        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static QuickRaceSetupModel Setup()
    {
        var setup = new QuickRaceSetupModel();
        setup.CustomClasses.Add(new QuickRaceClassModel
        {
            Name = "GT3",
            Entries =
            {
                new QuickRaceEntryModel { DriverName = "Me", IsHuman = true },
                new QuickRaceEntryModel { DriverName = "A", Car = "car_b" },
                new QuickRaceEntryModel { DriverName = "B", Car = "car_b" }
            }
        });
        setup.CustomClasses.Add(new QuickRaceClassModel
        {
            Name = "GT4",
            Entries = { new QuickRaceEntryModel { DriverName = "C", Car = "car_c" } }
        });

        return setup;
    }

    private static QuickRaceLaunchSnapshot Launch() => new()
    {
        SessionType = SessionType.Qualifying,
        Cars =
        {
            new QuickRaceLaunchCar { Index = 0, DriverName = "Me", Car = "car_a", ClassName = "GT3", IsHuman = true },
            new QuickRaceLaunchCar { Index = 1, DriverName = "A", Car = "car_b", ClassName = "GT3" },
            new QuickRaceLaunchCar { Index = 2, DriverName = "B", Car = "car_b", ClassName = "GT3" },
            new QuickRaceLaunchCar { Index = 3, DriverName = "C", Car = "car_c", ClassName = "GT4" }
        }
    };

    [Fact]
    public void BuildQualifyingRows_RanksByBestLapAndLeavesUntimedCarsLast()
    {
        var entries = new List<SessionResultEntry>
        {
            new() { Driver = "B", Car = "car_b", Skin = "s", BestLapTimeMs = "1:30.500" },
            new() { Driver = "Me", Car = "car_a", Skin = "s", BestLapTimeMs = "1:31.000" },
            new() { Driver = "C", Car = "car_c", Skin = "s", BestLapTimeMs = "1:40.000" }
        };

        var rows = QuickRaceService.BuildQualifyingRows(entries, Launch());

        Assert.Equal(new[] { "B", "Me", "C", "A" }, rows.Select(r => r.DriverName).ToArray());
        Assert.Equal(new[] { 1, 1, 1, 3 }.Length, rows.Count);
        Assert.Equal(new[] { 1, 2, 1, 3 }, rows.Select(r => r.ClassPosition).ToArray());
        Assert.Equal(90500, rows[0].BestLapTimeMs);
        Assert.Equal(0, rows[3].BestLapTimeMs);
    }

    [Fact]
    public void ApplyQualifying_ReordersEachColumnAndMarksQualifyingDone()
    {
        var setup = Setup();
        var rows = new List<QuickRaceResultRow>
        {
            new() { Position = 1, DriverName = "B", Car = "car_b", BestLapTimeMs = 90000 },
            new() { Position = 2, DriverName = "Me", Car = "car_a", BestLapTimeMs = 91000, IsHuman = true },
            new() { Position = 3, DriverName = "C", Car = "car_c", BestLapTimeMs = 99000 },
            new() { Position = 4, DriverName = "A", Car = "car_b" }
        };

        QuickRaceGrid.ApplyQualifying(setup, rows);

        Assert.True(setup.QualifyingCompleted);
        Assert.Equal(new[] { "B", "Me", "A" }, setup.CustomClasses[0].Entries.Select(e => e.DriverName).ToArray());
        Assert.Equal(90000, setup.CustomClasses[0].Entries[0].QualifyingTimeMs);
        Assert.Null(setup.CustomClasses[0].Entries[2].QualifyingTimeMs);

        QuickRaceGrid.ClearQualifying(setup);

        Assert.False(setup.QualifyingCompleted);
        Assert.All(setup.CustomClasses.SelectMany(c => c.Entries), e => Assert.Null(e.QualifyingTimeMs));
    }

    [Fact]
    public async Task ImportQualifying_ReordersTheSavedGridAndTellsTheListeners()
    {
        var context = _db.CreateContext();
        var repository = new QuickRaceRepository(context);
        await repository.SaveSetupJsonAsync(JsonSerializer.Serialize(Setup(), new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
        await repository.SaveLaunchJsonAsync(JsonSerializer.Serialize(Launch(), new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));

        var notified = new List<QuickRaceQualifyingModel>();
        var notifier = new RaceSessionChangeNotifier();
        notifier.QuickQualifyingImported += notified.Add;
        var service = new QuickRaceService(repository, new ImportedLuaResultRepository(context), notifier);

        var path = Path.Combine(_folder, "quick-race-261117-203015.json");
        File.WriteAllText(path, """
        [
          { "driver": "A", "car": "car_b", "skin": "s", "bestLapTimeMs": "1:29.000" },
          { "driver": "Me", "car": "car_a", "skin": "s", "bestLapTimeMs": "1:30.000" },
          { "driver": "B", "car": "car_b", "skin": "s", "bestLapTimeMs": "1:31.000" }
        ]
        """);

        var result = await service.ImportQualifyingFromPathAsync(path, null);

        Assert.Single(notified);
        Assert.Equal("A", result.Rows[0].DriverName);

        var saved = await new QuickRaceService(new QuickRaceRepository(_db.CreateContext()), new ImportedLuaResultRepository(_db.CreateContext()), notifier).GetSetupAsync();
        Assert.True(saved.QualifyingCompleted);
        Assert.Equal(new[] { "A", "Me", "B" }, saved.CustomClasses[0].Entries.Select(e => e.DriverName).ToArray());
    }
}

public class QuickRaceQualifyingKeptTests
{
    [Fact]
    public void AttachQualifying_PutsTheQualifyingPlaceAndTimeOnTheRaceRows()
    {
        var rows = new List<QuickRaceResultRow>
        {
            new() { Position = 1, DriverName = "Bot", Car = "car_b" },
            new() { Position = 2, DriverName = "Me", Car = "car_a", IsHuman = true }
        };
        var qualifying = new List<QuickRaceResultRow>
        {
            new() { Position = 1, DriverName = "Me", Car = "car_a", IsHuman = true, BestLapTimeMs = 90000 },
            new() { Position = 2, DriverName = "Bot", Car = "car_b" }
        };

        QuickRaceService.AttachQualifying(rows, qualifying);

        Assert.Equal(2, rows[0].QualifyingPosition);
        Assert.Null(rows[0].QualifyingTimeMs);
        Assert.Equal(1, rows[1].QualifyingPosition);
        Assert.Equal(90000, rows[1].QualifyingTimeMs);
    }
}
