using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MulticlassRace.Models;
using MulticlassRace.Services.QuickRace;
using MulticlassRace.Services.Races;
using MulticlassRace.Services.Results;
using MulticlassRace.Tests.Support;
using MulticlassRace.ViewModels;
using QuickRacePage = multi_class_race_dashboard.Components.Pages.QuickRace;

namespace MulticlassRace.Tests.Pages;

public class QuickRacePageTests : PageTestContext
{
    private sealed class RecordingLauncher : IQuickRaceLauncher
    {
        public int Launches { get; private set; }

        public SessionType? LastSession { get; private set; }

        public Task LaunchAsync(SessionType sessionType = SessionType.Race)
        {
            Launches++;
            LastSession = sessionType;
            return Task.CompletedTask;
        }
    }

    private readonly RecordingLauncher launcher = new();

    public QuickRacePageTests()
    {
        Services.AddScoped<IQuickRaceService, QuickRaceService>();
        Services.AddSingleton<IQuickRaceLauncher>(launcher);
        Services.AddSingleton<IRandomNameService>(new RandomNameService(new[] { "Ann" }, new[] { "Lee" }));

        var human = FakeDriverService.Driver("Max");
        human.IsHuman = true;
        human.Car = "ferrari_499p";
        human.CarDisplayName = "Ferrari 499P";
        human.Skin = "red";
        DriverService.Drivers.Add(human);
    }

    private IRenderedComponent<QuickRacePage> RenderPage() => RenderComponent<QuickRacePage>();

    private static void OpenTab(IRenderedComponent<QuickRacePage> cut, string label)
        => cut.FindAll(".wec-qr-tabs .wec-tab").First(t => t.TextContent.Trim() == label).Click();

    [Fact]
    public void Page_ShowsTheThreeTabs_WithResultDisabledUntilConfigured()
    {
        var cut = RenderPage();

        var tabs = cut.FindAll(".wec-qr-tabs .wec-tab");

        Assert.Equal(new[] { "Race Settings", "Drivers", "Result" }, tabs.Select(t => t.TextContent.Trim()).ToArray());
        Assert.True(tabs[2].HasAttribute("disabled"));
    }

    [Fact]
    public void RaceSettings_HaveNoRaceNameOrPointAndClassSetting_AndDefaultToTheHumanDriversCar()
    {
        var cut = RenderPage();

        Assert.Empty(cut.FindAll("#race-name"));
        Assert.Empty(cut.FindAll("#race-point-setting"));
        Assert.Empty(cut.FindAll("#race-class-setting"));
        Assert.Contains("Ferrari 499P", cut.Find("#qr-human-car").ParentElement!.TextContent);
        Assert.Contains("Max", cut.Markup);
    }

    [Fact]
    public void DriversTab_StartsWithOneClassHoldingTheHumanDriver()
    {
        var cut = RenderPage();

        OpenTab(cut, "Drivers");

        var rows = cut.FindAll(".wec-qr-col .wec-qr-row");
        var row = Assert.Single(rows);
        Assert.Contains("Max", row.TextContent);
        Assert.Contains("YOU", row.TextContent);
        Assert.Empty(row.QuerySelectorAll("button"));
    }

    [Fact]
    public void AddClass_AddsAColumnToTheCustomGrid()
    {
        var cut = RenderPage();
        OpenTab(cut, "Drivers");

        cut.FindAll(".wec-qr-mode .wec-toolbar__btn").First(b => b.TextContent.Contains("Add Class")).Click();

        Assert.Equal(2, cut.FindAll(".wec-qr-col").Count);
    }

    [Fact]
    public void ExistingClassSystem_ShowsOneColumnPerClassOfTheSetting()
    {
        var cut = RenderPage();
        OpenTab(cut, "Drivers");

        cut.FindAll(".wec-qr-mode__btn").Last().Click();

        cut.WaitForAssertion(() => Assert.True(cut.FindAll(".wec-qr-col").Count >= 2));
        Assert.NotEmpty(cut.FindAll("#qr-class-setting"));
    }

    [Fact]
    public void ConfiguringTrackAndAnOpponent_EnablesStartOnTheDriversTab_WhileResultWaitsForARace()
    {
        var cut = RenderPage();

        cut.Find("#qr-track").Change("monza");
        OpenTab(cut, "Drivers");
        cut.Find(".wec-qr-col__add").Click();
        cut.FindAll(".wec-qr-picker .wec-tab").First(t => t.TextContent.Contains("Custom Driver")).Click();
        cut.Find("#qr-custom-car").Change("porsche_963");
        cut.FindAll(".wec-qr-picker .wec-modal__btn--primary").Single().Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".wec-qr-col .wec-qr-row").Count));
        Assert.Contains("Ann Lee", cut.Find(".wec-qr-col").TextContent);

        Assert.True(cut.FindAll(".wec-qr-tabs .wec-tab").Last().HasAttribute("disabled"));

        cut.FindAll(".wec-qr-next .wec-toolbar__btn").Single(b => b.TextContent.Contains("Start Session")).Click();

        cut.WaitForAssertion(() => Assert.Equal(1, launcher.Launches));
        Assert.Contains("The race is running", cut.Markup);
    }

    [Fact]
    public void ImportedResult_ShowsPositionsWithoutPoints_AndJoinsTheHistory()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mcr-quick-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "quick-race-261117-203015.json");
            File.WriteAllText(path, """
            {
              "track": "monza",
              "players": [ { "name": "Max", "car": "ferrari_499p", "skin": "red" }, { "name": "Ann Lee", "car": "porsche_963", "skin": "penske" } ],
              "sessions": [ { "type": 3, "lapsCount": 5, "lapsTotal": [5, 5], "bestLaps": [ { "car": 0, "lap": 2, "time": 95000 } ], "raceResult": [1, 0] } ]
            }
            """);

            var cut = RenderPage();

            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IQuickRaceService>()
                .ImportResultFromPathAsync(path, null).GetAwaiter().GetResult();

            cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".wec-qr-table")));

            var headers = cut.FindAll(".wec-qr-table th").Select(h => h.TextContent.Trim()).ToArray();
            Assert.DoesNotContain("Points", headers);
            Assert.Contains("Ann Lee", cut.FindAll(".wec-qr-table tbody tr")[0].TextContent);
            Assert.Single(cut.FindAll(".wec-qr-history__item"));
            // The history shows the class position, not the overall one (Max finished 2nd overall, 1st in his class).
            Assert.Contains("P1", cut.Find(".wec-qr-history__pos").TextContent);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void AddCustomDriver(IRenderedComponent<QuickRacePage> cut)
    {
        cut.Find(".wec-qr-col__add").Click();
        cut.FindAll(".wec-qr-picker .wec-tab").First(t => t.TextContent.Contains("Custom Driver")).Click();
        cut.Find("#qr-custom-car").Change("porsche_963");
        cut.FindAll(".wec-qr-picker .wec-modal__btn--primary").Single().Click();
    }

    private static void SaveAsNewPreset(IRenderedComponent<QuickRacePage> cut, string name)
    {
        cut.Find("#qr-preset-name").Input(name);
        cut.FindAll(".wec-modal__btn--primary").Last().Click();
    }

    [Fact]
    public void Presets_DropdownIsDisabledUntilAPresetIsSaved()
    {
        var cut = RenderPage();
        OpenTab(cut, "Drivers");

        Assert.True(cut.Find("#qr-preset-select").HasAttribute("disabled"));

        cut.Find("#qr-preset-save").Click();
        Assert.Contains("Preset Name", cut.Find(".wec-modal").TextContent);
        SaveAsNewPreset(cut, "Le Mans grid");

        cut.WaitForAssertion(() => Assert.False(cut.Find("#qr-preset-select").HasAttribute("disabled")));
        Assert.Contains("Le Mans grid", cut.Find("#qr-preset-select").TextContent);
    }

    [Fact]
    public void Presets_LoadingAPreset_BringsBackItsDrivers()
    {
        var cut = RenderPage();
        OpenTab(cut, "Drivers");
        AddCustomDriver(cut);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".wec-qr-col .wec-qr-row").Count));

        cut.Find("#qr-preset-save").Click();
        SaveAsNewPreset(cut, "Two cars");
        cut.WaitForAssertion(() => Assert.False(cut.Find("#qr-preset-select").HasAttribute("disabled")));

        cut.FindAll(".wec-qr-row .wec-qr-icon--danger").Single().Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".wec-qr-col .wec-qr-row")));

        cut.Find("#qr-preset-select").Click();
        cut.FindAll(".wec-qr-presets__item .wec-qr-presets__load").Single().Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".wec-qr-col .wec-qr-row").Count));
        Assert.Contains("Ann Lee", cut.Find(".wec-qr-col").TextContent);
    }

    [Fact]
    public void Presets_SavingAgain_AsksToOverwrite_AndSaveAsAsksForANewName()
    {
        var cut = RenderPage();
        OpenTab(cut, "Drivers");

        cut.Find("#qr-preset-save").Click();
        SaveAsNewPreset(cut, "First");
        cut.WaitForAssertion(() => Assert.Contains("First", cut.Find("#qr-preset-select").TextContent));

        cut.Find("#qr-preset-save").Click();
        Assert.Contains("Overwrite the preset \"First\"", cut.Find(".wec-modal").TextContent);
        cut.Find(".wec-modal__btn--danger").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wec-modal")));

        cut.Find("#qr-preset-save-menu").Click();
        cut.Find("#qr-preset-save-as").Click();
        SaveAsNewPreset(cut, "Second");
        cut.WaitForAssertion(() => Assert.Contains("Second", cut.Find("#qr-preset-select").TextContent));

        cut.Find("#qr-preset-select").Click();
        Assert.Equal(new[] { "First", "Second" },
            cut.FindAll(".wec-qr-presets__item .wec-qr-presets__load").Select(b => b.TextContent.Trim()).ToArray());
    }
}

public class QuickRaceQualifyingPageTests : PageTestContext
{
    private sealed class RecordingLauncher : IQuickRaceLauncher
    {
        public List<SessionType> Sessions { get; } = new();

        public Task LaunchAsync(SessionType sessionType = SessionType.Race)
        {
            Sessions.Add(sessionType);
            return Task.CompletedTask;
        }
    }

    private readonly RecordingLauncher launcher = new();

    public QuickRaceQualifyingPageTests()
    {
        Services.AddScoped<IQuickRaceService, QuickRaceService>();
        Services.AddSingleton<IQuickRaceLauncher>(launcher);
        Services.AddSingleton<IRandomNameService>(new RandomNameService(new[] { "Ann" }, new[] { "Lee" }));

        var human = FakeDriverService.Driver("Max");
        human.IsHuman = true;
        human.Car = "ferrari_499p";
        human.CarDisplayName = "Ferrari 499P";
        human.Skin = "red";
        DriverService.Drivers.Add(human);
    }

    private static void OpenTab(IRenderedComponent<QuickRacePage> cut, string label)
        => cut.FindAll(".wec-qr-tabs .wec-tab").First(t => t.TextContent.Trim() == label).Click();

    [Fact]
    public void WithQualifyingOn_TheDriversTabHintsAtQualifyingAndStartsIt()
    {
        var cut = RenderComponent<QuickRacePage>();

        cut.Find("#qr-track").Change("monza");
        Assert.Empty(cut.FindAll("#qr-qualifying-minutes"));
        cut.Find("#qr-qualifying").Change(true);
        Assert.NotEmpty(cut.FindAll("#qr-qualifying-minutes"));

        OpenTab(cut, "Drivers");
        cut.Find(".wec-qr-col__add").Click();
        cut.FindAll(".wec-qr-picker .wec-tab").First(t => t.TextContent.Contains("Custom Driver")).Click();
        cut.Find("#qr-custom-car").Change("porsche_963");
        cut.FindAll(".wec-qr-picker .wec-modal__btn--primary").Single().Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".wec-qr-col .wec-qr-row").Count));

        Assert.Contains("You must qualify first", cut.Find(".wec-qr-qualhint").TextContent);

        var start = cut.FindAll(".wec-qr-next .wec-toolbar__btn").Single(b => b.TextContent.Contains("Start Qualifying"));
        start.Click();

        cut.WaitForAssertion(() => Assert.Equal(new[] { SessionType.Qualifying }, launcher.Sessions));
    }
}

public class QuickRaceRunningNoteTests : PageTestContext
{
    public QuickRaceRunningNoteTests()
    {
        Services.AddScoped<IQuickRaceService, QuickRaceService>();
        Services.AddSingleton<IQuickRaceLauncher>(new NoopLauncher());
        Services.AddSingleton<IRandomNameService>(new RandomNameService(new[] { "Ann" }, new[] { "Lee" }));

        var human = FakeDriverService.Driver("Max");
        human.IsHuman = true;
        human.Car = "ferrari_499p";
        human.Skin = "red";
        DriverService.Drivers.Add(human);
    }

    private sealed class NoopLauncher : IQuickRaceLauncher
    {
        public Task LaunchAsync(SessionType sessionType = SessionType.Race) => Task.CompletedTask;
    }

    [Fact]
    public void RunningNote_GoesAwayWhenTheGameClosesWithoutAResult()
    {
        var cut = RenderComponent<QuickRacePage>();
        cut.Find("#qr-track").Change("monza");
        cut.FindAll(".wec-qr-tabs .wec-tab")[1].Click();
        cut.Find(".wec-qr-col__add").Click();
        cut.FindAll(".wec-qr-picker .wec-tab").First(t => t.TextContent.Contains("Custom Driver")).Click();
        cut.Find("#qr-custom-car").Change("porsche_963");
        cut.FindAll(".wec-qr-picker .wec-modal__btn--primary").Single().Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".wec-qr-col .wec-qr-row").Count));

        cut.FindAll(".wec-qr-next .wec-toolbar__btn").Single(b => b.TextContent.Contains("Start Session")).Click();
        cut.WaitForAssertion(() => Assert.Contains("The race is running", cut.Markup));

        Services.GetRequiredService<LuaResultImportState>()
            .Report(new LuaResultImportOutcome(false, "Could not find the session result. Please import it manually.", true));

        cut.WaitForAssertion(() => Assert.DoesNotContain("The race is running", cut.Markup));
    }
}
