using MulticlassRace.Data.Seed;
using Bunit;
using Microsoft.EntityFrameworkCore;
using multi_class_race_dashboard.Components.Pages;
using MulticlassRace.Models;
using MulticlassRace.Tests.Support;
using MulticlassRace.ViewModels;
using MulticlassRace.Services.Drivers;
using MulticlassRace.Services.Notifications;

namespace MulticlassRace.Tests.Pages;

public class TeamsPageTests : PageTestContext
{
    private static readonly Guid Hypercar = new(SeedIds.DefaultHypercarItemId);

    private async Task SeedTeamsAsync(params string[] names)
    {
        await using var context = Db.CreateContext();
        var hypercar = await context.ClassSettingItems.SingleAsync(i => i.ClassSettingItemId == Hypercar);

        foreach (var name in names)
        {
            context.Teams.Add(new Team { TeamId = Guid.NewGuid(), TeamName = name, ClassSettingItems = { hypercar } });
        }

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Renders_TeamsWithInitialsFallbackAndClassBadge()
    {
        await SeedTeamsAsync("Ferrari");

        var cut = RenderComponent<Teams>();

        cut.WaitForElement("tbody tr");
        Assert.Equal("FE", cut.Find(".wec-table__logo--fallback").TextContent);
        Assert.Equal("Hypercar", cut.Find(".wec-badge").TextContent);
        Assert.Equal("Active", cut.Find(".wec-status").TextContent.Trim());
    }

    [Fact]
    public async Task Search_FiltersByTeamNameAndResetsToFirstPage()
    {
        await SeedTeamsAsync("Ferrari", "Porsche");
        var cut = RenderComponent<Teams>();
        cut.WaitForElement("tbody tr");

        cut.Find("#filter-team-search").Input("porsc");

        Assert.Single(cut.FindAll("tbody tr"));
        Assert.Contains("Porsche", cut.Find("tbody").TextContent);
    }

    [Fact]
    public async Task Search_MatchesDriverNameOfTeam()
    {
        await SeedTeamsAsync("Ferrari", "Porsche");
        Guid porscheId;
        await using (var context = Db.CreateContext())
        {
            porscheId = (await context.Teams.SingleAsync(t => t.TeamName == "Porsche")).TeamId;
        }

        DriverService.Drivers.Add(FakeDriverService.Driver("Alonso", porscheId, "Porsche"));
        var cut = RenderComponent<Teams>();
        cut.WaitForElement("tbody tr");

        cut.Find("#filter-team-search").Input("alonso");

        Assert.Contains("Porsche", cut.Find("tbody").TextContent);
        Assert.DoesNotContain("Ferrari", cut.Find("tbody").TextContent);
    }

    [Fact]
    public async Task ClassFilter_ListsClassesAndFilters()
    {
        await SeedTeamsAsync("Ferrari");
        var cut = RenderComponent<Teams>();
        cut.WaitForElement("tbody tr");

        Assert.Contains(cut.FindAll("#filter-team-class option"), o => o.TextContent == "WEC - Hypercar");

        cut.Find("#filter-team-class").Change("WEC - GT3");

        Assert.Equal("No teams found", cut.Find("td.wec-empty").TextContent);
    }

    [Fact]
    public async Task Pagination_ShowsSecondPageOfEleventhTeam()
    {
        await SeedTeamsAsync(Enumerable.Range(1, 11).Select(i => $"Team {i:00}").ToArray());
        var cut = RenderComponent<Teams>();
        cut.WaitForElement("tbody tr");

        Assert.Equal(10, cut.FindAll("tbody tr").Count);
        Assert.Contains("Page 1 of 2", cut.Find(".wec-pagination__info").TextContent);

        cut.FindAll(".wec-pagination__btn")[1].Click();

        Assert.Single(cut.FindAll("tbody tr"));
        Assert.Equal("11", cut.Find("td.wec-table__position").TextContent);
    }

    [Fact]
    public void NewTeam_WithoutName_ShowsToast()
    {
        ToastMessage? toast = null;
        Toast.ToastChanged += message => toast = message;
        var cut = RenderComponent<Teams>();
        cut.WaitForElement(".wec-toolbar__btn").Click();

        cut.Find(".wec-modal__btn--primary").Click();

        Assert.Equal("Team name is required.", toast?.Message);
    }

    [Fact]
    public void NewTeam_WithoutClass_ShowsToast()
    {
        ToastMessage? toast = null;
        Toast.ToastChanged += message => toast = message;
        var cut = RenderComponent<Teams>();
        cut.WaitForElement(".wec-toolbar__btn").Click();
        cut.Find("#team-name").Change("Alpine");

        cut.Find(".wec-modal__btn--primary").Click();

        Assert.Equal("Please select at least one team class.", toast?.Message);
    }

    [Fact]
    public async Task NewTeam_WithClassAndDriver_IsCreated()
    {
        DriverService.Drivers.Add(FakeDriverService.Driver("Free Driver"));
        var cut = RenderComponent<Teams>();
        cut.WaitForElement(".wec-toolbar__btn").Click();
        cut.Find("#team-name").Change("Alpine");

        cut.Find("#team-class-search").Focus();
        cut.Find(".wec-class-search__option").Click();
        cut.Find(".wec-drivers__add").Click();
        cut.WaitForElement(".wec-drivers__result").Click();
        cut.Find(".wec-modal__btn--primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".wec-modal"));
            Assert.Contains("Alpine", cut.Find("tbody").TextContent);
        });

        await using var context = Db.CreateContext();
        Assert.Single(await context.Teams.Where(t => t.TeamName == "Alpine").ToListAsync());
    }

    [Fact]
    public async Task EditTeam_ShowsReadOnlyIdAndExistingClassChip()
    {
        await SeedTeamsAsync("Ferrari");
        var cut = RenderComponent<Teams>();
        cut.WaitForElement("button[title='Edit Ferrari']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#team-id")));
        Assert.Equal("WEC - Hypercar", cut.Find(".wec-class-tags__chip-text").TextContent);
        Assert.Equal("Edit Team", cut.Find(".wec-modal__header span").TextContent);
    }

    [Fact]
    public async Task EditTeam_RemovingClassChip_RemovesIt()
    {
        await SeedTeamsAsync("Ferrari");
        var cut = RenderComponent<Teams>();
        cut.WaitForElement("button[title='Edit Ferrari']").Click();
        cut.WaitForElement(".wec-class-tags__chip-remove").Click();

        Assert.Empty(cut.FindAll(".wec-class-tags__chip"));
    }
}
