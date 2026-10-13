using MulticlassRace.Data.Seed;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using multi_class_race_dashboard.Components.Pages;
using MulticlassRace.Data;
using MulticlassRace.Models;
using MulticlassRace.Tests.Support;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.AssettoCorsa;
using MulticlassRace.Services.Races;

namespace MulticlassRace.Tests.Pages;

public class RacesPageTests : PageTestContext
{
    private static readonly Guid WecSetting = new(SeedIds.DefaultClassSettingId);
    private static readonly Guid Hypercar = new(SeedIds.DefaultHypercarItemId);

    private Guid championshipId;
    private Guid seasonId;

    private async Task SeedSeasonAsync()
    {
        await using var context = Db.CreateContext();
        var championship = new MulticlassRace.Models.Championship { ChampionshipId = Guid.NewGuid(), ChampionshipName = "WEC 2026" };
        var season = new Season { SeasonId = Guid.NewGuid(), SeasonName = "Season 1", Championship = championship };
        context.Seasons.Add(season);
        await context.SaveChangesAsync();

        championshipId = championship.ChampionshipId;
        seasonId = season.SeasonId;
    }

    private async Task<Guid> SeedTeamAsync(string name)
    {
        await using var context = Db.CreateContext();
        var hypercar = await context.ClassSettingItems.SingleAsync(i => i.ClassSettingItemId == Hypercar);
        var team = new Team { TeamId = Guid.NewGuid(), TeamName = name, ClassSettingItems = { hypercar } };
        context.Teams.Add(team);
        await context.SaveChangesAsync();
        return team.TeamId;
    }

    private IRenderedComponent<Races> RenderSelected()
    {
        Services.GetRequiredService<FakeNavigationManager>()
            .NavigateTo($"/races?championshipId={championshipId}&seasonId={seasonId}");

        var cut = RenderComponent<Races>();
        cut.WaitForElement(".wec-toolbar");
        return cut;
    }

    [Fact]
    public void WithoutSelection_AsksForChampionshipAndSeason()
    {
        var cut = RenderComponent<Races>();

        Assert.Equal("Select a championship and a season to view races.", cut.WaitForElement("td.wec-empty").TextContent);
        Assert.Empty(cut.FindAll(".wec-toolbar"));
    }

    [Fact]
    public async Task ChoosingChampionshipThenSeason_LoadsRaces()
    {
        await SeedSeasonAsync();
        RaceService.AddRace(seasonId, "Spa 6h");
        var cut = RenderComponent<Races>();
        cut.WaitForElement("#filter-championship");

        Assert.Empty(cut.FindAll("#filter-season"));

        cut.Find("#filter-championship").Change(championshipId.ToString());
        cut.WaitForElement("#filter-season").Change(seasonId.ToString());

        cut.WaitForAssertion(() => Assert.Contains("Spa 6h", cut.Find("tbody").TextContent));
        Assert.Contains("WEC 2026", cut.Find(".wec-breadcrumb").TextContent);
    }

    [Fact]
    public async Task QueryString_PreselectsChampionshipAndSeason()
    {
        await SeedSeasonAsync();
        RaceService.AddRace(seasonId, "Le Mans");

        var cut = RenderSelected();

        cut.WaitForAssertion(() => Assert.Contains("Le Mans", cut.Find("tbody").TextContent));
        Assert.Equal(championshipId.ToString(), cut.Find("#filter-championship").GetAttribute("value"));
    }

    [Fact]
    public async Task SeasonWithoutRaces_ShowsCreateHint()
    {
        await SeedSeasonAsync();

        var cut = RenderSelected();

        Assert.Equal("No races found. Click Add Race to create one.", cut.Find("td.wec-empty").TextContent);
    }

    [Fact]
    public async Task Sorting_ByRaceTogglesOrder()
    {
        await SeedSeasonAsync();
        RaceService.AddRace(seasonId, "Alpha");
        RaceService.AddRace(seasonId, "Beta");
        var cut = RenderSelected();

        IEnumerable<string> Names() => cut.FindAll("tbody tr td:first-child").Select(td => td.TextContent);
        Assert.Equal(new[] { "Alpha", "Beta" }, Names());

        cut.Find("th .wec-table__sort").Click();

        Assert.Equal(new[] { "Beta", "Alpha" }, Names());
    }

    [Fact]
    public async Task AddRace_ValidatesNameThenTrack()
    {
        await SeedSeasonAsync();
        var toasts = new List<string>();
        Toast.ToastChanged += message => toasts.Add(message.Message);
        var cut = RenderSelected();

        cut.Find(".wec-toolbar .wec-toolbar__btn:nth-child(2)").Click();
        cut.Find(".wec-modal__btn--primary").Click();
        cut.Find("#race-name").Change("Monza 6h");
        cut.Find(".wec-modal__btn--primary").Click();

        Assert.Equal(new[] { "Race name is required.", "Select a track." }, toasts);
    }

    [Fact]
    public async Task AddRace_DefaultsToDefaultPointAndClassSettings()
    {
        await SeedSeasonAsync();
        var cut = RenderSelected();

        cut.Find(".wec-toolbar .wec-toolbar__btn:nth-child(2)").Click();

        Assert.Contains("Standard (default)", cut.Find("#race-point-setting").TextContent);
        Assert.Equal("WEC (default)", cut.Find("#race-class-setting option[selected], #race-class-setting option:nth-child(2)").TextContent);
        Assert.Contains("1:25", cut.Find("#race-point-setting").ParentElement!.TextContent);
    }

    [Fact]
    public async Task AddRace_PickingTrackWithLayoutsRequiresLayout()
    {
        await SeedSeasonAsync();
        var toasts = new List<string>();
        Toast.ToastChanged += message => toasts.Add(message.Message);
        var cut = RenderSelected();
        cut.Find(".wec-toolbar .wec-toolbar__btn:nth-child(2)").Click();

        cut.Find("#race-name").Change("Spa");
        cut.Find("#race-track").Change("spa");

        cut.WaitForAssertion(() => Assert.True(cut.Find("#race-track-layout").HasAttribute("required")));
        Assert.Contains(cut.FindAll("#race-track-layout option"), o => o.TextContent == "Select layout...");

        cut.Find(".wec-modal__btn--primary").Click();
        Assert.Equal("Select a track layout.", toasts.Last());

        cut.Find("#race-track-layout").Change("gp");
        cut.Find(".wec-modal__btn--primary").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wec-modal")));
        var created = Assert.Single(RaceService.Created);
        Assert.Equal("spa", created.TrackName);
        Assert.Equal("gp", created.TrackLayout);
        Assert.Equal(900, created.RaceDuration);
        Assert.Equal(20, created.PracticeSessionMinutes);
    }

    [Fact]
    public async Task AddRace_ShowsEstimatedTimeFromBestLap()
    {
        await SeedSeasonAsync();
        var cut = RenderSelected();
        cut.Find(".wec-toolbar .wec-toolbar__btn:nth-child(2)").Click();

        Assert.Contains("No lap data recorded", cut.Find("#race-laps").ParentElement!.TextContent);

        cut.Find("#race-track").Change("monza");

        cut.WaitForAssertion(() => Assert.Contains("Estimated race time: 15m 00s", cut.Find("#race-laps").ParentElement!.TextContent));
    }

    [Fact]
    public async Task EditRace_PrefillsFormAndSaves()
    {
        await SeedSeasonAsync();
        RaceService.AddRace(seasonId, "Monza", "monza", WecSetting);
        var cut = RenderSelected();

        cut.Find("button[title='Edit Monza']").Click();
        cut.WaitForElement("#race-name");

        Assert.Equal("Edit Race", cut.Find(".wec-modal__header span").TextContent);
        Assert.Equal("Monza", cut.Find("#race-name").GetAttribute("value"));
    }

    [Fact]
    public async Task DeleteRace_ConfirmsAndRemovesRow()
    {
        await SeedSeasonAsync();
        var race = RaceService.AddRace(seasonId, "Doomed");
        var cut = RenderSelected();

        cut.Find("button[title='Delete Doomed']").Click();
        Assert.Contains("Delete Doomed?", cut.Find(".wec-modal__body").TextContent);
        cut.Find(".wec-modal__btn--danger").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("td.wec-empty")));
        Assert.Equal(new[] { race.RaceId }, RaceService.Deleted);
    }

    [Fact]
    public async Task ClickingRaceRow_OpensSessionsOnTeamsTabWhenNoTeamsEntered()
    {
        await SeedSeasonAsync();
        await SeedTeamAsync("Ferrari");
        RaceService.AddRace(seasonId, "Monza", "monza", WecSetting);
        var cut = RenderSelected();

        cut.Find("tbody tr").Click();

        cut.WaitForAssertion(() => Assert.Contains("Sessions — Monza", cut.Find(".wec-modal__header span").TextContent));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".wec-race-teams")));
        Assert.Contains("wec-tab--active", cut.FindAll(".wec-tab")[0].ClassList);
        Assert.All(cut.FindAll(".wec-tab").Skip(1), tab => Assert.True(tab.HasAttribute("disabled")));
        Assert.Contains("Ferrari", cut.Find(".wec-race-teams__list").TextContent);
        Assert.Equal("Hypercar", cut.Find(".wec-race-teams__row .wec-badge").TextContent);
    }

    [Fact]
    public async Task EnteringTeam_SavesItAndUnlocksPracticeAndQualifying()
    {
        await SeedSeasonAsync();
        var teamId = await SeedTeamAsync("Ferrari");
        var race = RaceService.AddRace(seasonId, "Monza", "monza", WecSetting);
        var cut = RenderSelected();
        cut.Find("tbody tr").Click();
        cut.WaitForElement(".wec-race-teams__row input").Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { teamId }, RaceService.TeamsByRace[race.RaceId]);
            Assert.False(cut.FindAll(".wec-tab")[1].HasAttribute("disabled"));
            Assert.False(cut.FindAll(".wec-tab")[2].HasAttribute("disabled"));
        });
        Assert.True(cut.FindAll(".wec-tab")[3].HasAttribute("disabled"));
    }

    [Fact]
    public async Task SelectAllVisible_EntersEveryTeamAndTogglesLabel()
    {
        await SeedSeasonAsync();
        var a = await SeedTeamAsync("A");
        var b = await SeedTeamAsync("B");
        var race = RaceService.AddRace(seasonId, "Monza", "monza", WecSetting);
        var cut = RenderSelected();
        cut.Find("tbody tr").Click();
        cut.WaitForElement(".wec-race-teams__select-all input").Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new HashSet<Guid> { a, b }, RaceService.TeamsByRace[race.RaceId]);
            Assert.Equal("Unselect all visible", cut.Find(".wec-race-teams__select-all span").TextContent);
        });
    }

    [Fact]
    public async Task RaceWithEnteredTeams_OpensOnPracticeTabWithControls()
    {
        await SeedSeasonAsync();
        var teamId = await SeedTeamAsync("Ferrari");
        var race = RaceService.AddRace(seasonId, "Monza", "monza", WecSetting);
        RaceService.TeamsByRace[race.RaceId] = new HashSet<Guid> { teamId };
        var cut = RenderSelected();

        cut.Find("tbody tr").Click();

        cut.WaitForElement("#race-session-weather");
        Assert.Contains("wec-tab--active", cut.FindAll(".wec-tab")[1].ClassList);
        Assert.Equal(new[] { "Clear", "Heavy Clouds", "Heavy Fog", "Light Clouds", "Light Fog", "Mid Clear", "Mid Clouds" },
            cut.FindAll("#race-session-weather option").Select(o => o.TextContent));
        Assert.Equal("Start Session", cut.Find(".wec-btn-title button").TextContent.Trim());
        Assert.False(cut.Find(".wec-btn-title button").HasAttribute("disabled"));
    }

    [Fact]
    public async Task StartSession_LaunchesWithChosenConditions()
    {
        await SeedSeasonAsync();
        var teamId = await SeedTeamAsync("Ferrari");
        var race = RaceService.AddRace(seasonId, "Monza", "monza", WecSetting);
        RaceService.TeamsByRace[race.RaceId] = new HashSet<Guid> { teamId };
        var cut = RenderSelected();
        cut.Find("tbody tr").Click();
        cut.WaitForElement("#race-session-weather").Change("1_heavy_fog");
        cut.Find("#race-session-time-of-day").Change("Sunset (18:00)");
        cut.Find(".wec-penalties-setting input").Change(false);

        cut.Find(".wec-btn-title button").Click();

        cut.WaitForAssertion(() => Assert.Single(RaceService.Launched));
        Assert.Equal((race.RaceId, SessionType.Practice, "1_heavy_fog", 80d, false), RaceService.Launched[0]);
    }

    [Fact]
    public async Task StartSession_BlockedWhenCarsExceedPits()
    {
        await SeedSeasonAsync();
        var first = await SeedTeamAsync("Ferrari");
        var second = await SeedTeamAsync("Porsche");
        var race = RaceService.AddRace(seasonId, "Monza", "monza", WecSetting);
        RaceService.TeamsByRace[race.RaceId] = new HashSet<Guid> { first, second };
        TrackService.PitCount = 1;
        var cut = RenderSelected();
        cut.Find("tbody tr").Click();
        cut.WaitForElement(".wec-btn-title");

        Assert.True(cut.Find(".wec-btn-title button").HasAttribute("disabled"));
        Assert.Contains("number of cars (2) is more than the number of pits (1)", cut.Find(".wec-btn-title").GetAttribute("title"));
    }

    [Fact]
    public async Task SessionWithResults_ShowsStandingsTableAndFiltersByClass()
    {
        await SeedSeasonAsync();
        var teamId = await SeedTeamAsync("Ferrari");
        var race = RaceService.AddRace(seasonId, "Monza", "monza", WecSetting);
        RaceService.TeamsByRace[race.RaceId] = new HashSet<Guid> { teamId };
        RaceService.SessionsByRace[race.RaceId] = new List<SessionModel>
        {
            new()
            {
                SessionType = SessionType.Qualifying,
                SessionDate = new DateTime(2026, 3, 4),
                DriverStandings =
                {
                    new DriverStandingModel { Position = 1, DriverName = "Alonso", TeamName = "Ferrari", TeamClassName = "Hypercar", BestLapTimeMs = 83456 },
                    new DriverStandingModel { Position = 2, DriverName = "Button", TeamName = "McLaren", TeamClassName = "GT3", BestLapTimeMs = 0 }
                }
            }
        };
        var cut = RenderSelected();

        cut.Find("tbody tr").Click();

        cut.WaitForAssertion(() => Assert.Equal("Race", cut.Find(".wec-tab.wec-tab--active").TextContent));
        Assert.Contains("No Race sessions for this race.", cut.Find(".wec-empty").TextContent);

        cut.FindAll(".wec-tab")[2].Click();

        cut.WaitForElement(".wec-session-block table");
        Assert.Equal(new[] { "Alonso", "Button" }, cut.FindAll(".wec-session-block tbody tr td:nth-child(2)").Select(td => td.TextContent));
        Assert.Contains("1:23.456", cut.Find(".wec-session-block tbody").TextContent);

        cut.Find("#standing-class-filter").Change("GT3");

        Assert.Equal(new[] { "Button" }, cut.FindAll(".wec-session-block tbody tr td:nth-child(2)").Select(td => td.TextContent));
        Assert.Contains("—", cut.Find(".wec-session-block tbody").TextContent);
    }

    [Fact]
    public async Task ClosingSessionsModal_RemovesItAndKeepsList()
    {
        await SeedSeasonAsync();
        RaceService.AddRace(seasonId, "Monza", "monza", WecSetting);
        var cut = RenderSelected();
        cut.Find("tbody tr").Click();
        cut.WaitForElement(".wec-modal__close").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wec-modal")));
        Assert.Contains("Monza", cut.Find("tbody").TextContent);
    }
}
