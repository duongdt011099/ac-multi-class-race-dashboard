using Bunit;
using multi_class_race_dashboard.Components.Pages;
using MulticlassRace.Tests.Support;
using MulticlassRace.Services.Notifications;

namespace MulticlassRace.Tests.Pages;

public class ChampionshipPageTests : PageTestContext
{
    private static void Create(IRenderedComponent<Championship> cut, string name)
    {
        cut.Find(".wec-toolbar__btn").Click();
        cut.Find("#championship-name").Change(name);
        cut.Find(".wec-modal__btn--primary").Click();
    }

    [Fact]
    public void Renders_EmptyStateWhenNoChampionships()
    {
        var cut = RenderComponent<Championship>();

        cut.WaitForAssertion(() => Assert.Equal("No championships found", cut.Find("td.wec-empty").TextContent));
    }

    [Fact]
    public void Create_AddsRowAndClosesModal()
    {
        var cut = RenderComponent<Championship>();
        cut.WaitForElement(".wec-toolbar__btn");

        Create(cut, "WEC 2026");

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".wec-modal"));
            Assert.Contains("WEC 2026", cut.Find("tbody").TextContent);
        });
    }

    [Fact]
    public void Create_WithoutName_ShowsRequiredToast()
    {
        ToastMessage? toast = null;
        Toast.ToastChanged += message => toast = message;

        var cut = RenderComponent<Championship>();
        cut.WaitForElement(".wec-toolbar__btn");
        Create(cut, "  ");

        Assert.Equal("Championship name is required.", toast?.Message);
        Assert.NotEmpty(cut.FindAll(".wec-modal"));
    }

    [Fact]
    public void Sorting_ByChampionshipTogglesDirection()
    {
        var cut = RenderComponent<Championship>();
        cut.WaitForElement(".wec-toolbar__btn");
        Create(cut, "Alpha");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wec-modal")));
        Create(cut, "Beta");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wec-modal")));

        IEnumerable<string> Names() => cut.FindAll("tbody tr td:nth-child(2)").Select(td => td.TextContent);
        Assert.Equal(new[] { "Alpha", "Beta" }, Names());

        cut.Find("th .wec-table__sort").Click();

        Assert.Equal(new[] { "Beta", "Alpha" }, Names());
    }

    [Fact]
    public void ClickingRow_OpensSeasonsModalAndAddsSeason()
    {
        var cut = RenderComponent<Championship>();
        cut.WaitForElement(".wec-toolbar__btn");
        Create(cut, "WEC");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wec-modal")));

        cut.Find("tbody tr").Click();
        cut.WaitForAssertion(() => Assert.Contains("No seasons yet", cut.Markup));
        Assert.Contains("WEC — Seasons", cut.Find(".wec-modal__header span").TextContent);

        cut.Find(".wec-seasons__add input").Input("2026");
        cut.Find(".wec-seasons__add .wec-toolbar__btn").Click();

        cut.WaitForAssertion(() => Assert.Equal("2026", cut.Find(".wec-seasons__name").TextContent));
    }

    [Fact]
    public void DeleteChampionship_ConfirmsAndRemovesRow()
    {
        var cut = RenderComponent<Championship>();
        cut.WaitForElement(".wec-toolbar__btn");
        Create(cut, "Gone");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wec-modal")));

        cut.Find("button[title='Delete Gone']").Click();
        cut.Find(".wec-modal__btn--danger").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("td.wec-empty")));
    }
}
