using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MulticlassRace.Models;
using MulticlassRace.Services.Tour;
using MulticlassRace.Tests.Support;
using multi_class_race_dashboard.Components;

namespace MulticlassRace.Tests.Components;

public class GuidedTourTests : PageTestContext
{
    private readonly string _script;

    public GuidedTourTests()
    {
        _script = Path.Combine(Path.GetTempPath(), "mcr-tour-ui-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(_script, """
            {
              "welcome": { "title": "Hi Rex", "message": "Tour?", "startLabel": "Go", "declineLabel": "Never" },
              "steps": [
                { "id": "one", "title": "One", "message": "First step" },
                { "id": "two", "title": "Two", "message": "Second step" },
                { "id": "three", "title": "Three", "message": "Last step" }
              ]
            }
            """);

        Services.AddSingleton<ITourDefinitionProvider>(new TourDefinitionProvider(_script, NullLogger<TourDefinitionProvider>.Instance));
        Services.AddScoped<GuidedTourRequest>();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && File.Exists(_script))
        {
            File.Delete(_script);
        }

        base.Dispose(disposing);
    }

    private IRenderedComponent<GuidedTour> RenderTour(string? storedDoneFlag = null)
    {
        if (storedDoneFlag is not null)
        {
            Storage.SetAsync("wec.tour.done", storedDoneFlag).GetAwaiter().GetResult();
        }

        JSInterop.Setup<string?>("wecTour.focus", _ => true).SetResult("right");

        var cut = RenderComponent<GuidedTour>();
        cut.WaitForState(() => true);
        return cut;
    }

    [Fact]
    public void FirstLaunchWithNoData_AsksWhetherToTakeTheTour()
    {
        var cut = RenderTour();

        cut.WaitForAssertion(() => Assert.Contains("Hi Rex", cut.Markup));
        Assert.Contains("Go", cut.Markup);
        Assert.Contains("Never", cut.Markup);
    }

    [Fact]
    public async Task AlreadyAnswered_StaysHidden()
    {
        var cut = RenderTour(storedDoneFlag: "1");

        await Task.Delay(100);
        Assert.Empty(cut.FindAll(".wec-tour"));
    }

    [Fact]
    public async Task ExistingData_StaysHidden()
    {
        await using (var context = Db.CreateContext())
        {
            context.Championships.Add(new Championship { ChampionshipName = "WEC", IsActive = true });
            await context.SaveChangesAsync();
        }

        var cut = RenderTour();

        await Task.Delay(100);
        Assert.Empty(cut.FindAll(".wec-tour"));
    }

    [Fact]
    public void Declining_HidesTheTour_AndRemembersIt()
    {
        var cut = RenderTour();
        cut.WaitForAssertion(() => Assert.Contains("Hi Rex", cut.Markup));

        cut.Find(".wec-tour__btn--ghost").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wec-tour")));
        Assert.Equal("1", Storage.GetAsync("wec.tour.done").GetAwaiter().GetResult());
    }

    [Fact]
    public void Walkthrough_AdvancesSkipsAndFinishes()
    {
        var cut = RenderTour();
        cut.WaitForAssertion(() => Assert.Contains("Hi Rex", cut.Markup));

        cut.Find(".wec-tour__btn--primary").Click();
        cut.WaitForAssertion(() => Assert.Contains("First step", cut.Markup));

        // Skip step moves on without doing anything.
        cut.FindAll(".wec-tour__btn--ghost").Single(b => b.TextContent == "Skip step").Click();
        cut.WaitForAssertion(() => Assert.Contains("Second step", cut.Markup));

        cut.FindAll(".wec-tour__btn--ghost").Single(b => b.TextContent == "Back").Click();
        cut.WaitForAssertion(() => Assert.Contains("First step", cut.Markup));

        cut.Find(".wec-tour__btn--primary").Click();
        cut.Find(".wec-tour__btn--primary").Click();
        cut.WaitForAssertion(() => Assert.Contains("Last step", cut.Markup));

        // The last step has no skip controls, just Finish.
        Assert.Empty(cut.FindAll(".wec-tour__link"));
        cut.Find(".wec-tour__btn--primary").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wec-tour")));
        Assert.Equal("1", Storage.GetAsync("wec.tour.done").GetAwaiter().GetResult());
    }

    [Fact]
    public void SkipTheWholeTour_EndsItImmediately()
    {
        var cut = RenderTour();
        cut.WaitForAssertion(() => Assert.Contains("Hi Rex", cut.Markup));
        cut.Find(".wec-tour__btn--primary").Click();
        cut.WaitForAssertion(() => Assert.Contains("First step", cut.Markup));

        cut.Find(".wec-tour__link").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wec-tour")));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "wecTour.clear");
    }

    [Fact]
    public void AnsweredTour_IsRememberedByANewStoreOnTheSameFile()
    {
        var cut = RenderTour();
        cut.WaitForAssertion(() => Assert.Contains("Hi Rex", cut.Markup));
        cut.Find(".wec-tour__btn--ghost").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".wec-tour")));

        // A new launch reads the same file, whatever port the server ends up on.
        var nextLaunch = new MulticlassRace.Services.Browser.BrowserStorage(Path.Combine(UploadsRoot, "preferences.json"));
        Assert.Equal("1", nextLaunch.GetAsync("wec.tour.done").GetAwaiter().GetResult());
    }

    [Fact]
    public void SidebarRequest_RestartsTheTourEvenAfterItWasDone()
    {
        var cut = RenderTour(storedDoneFlag: "1");

        Services.GetRequiredService<GuidedTourRequest>().Request();

        cut.WaitForAssertion(() => Assert.Contains("First step", cut.Markup));
    }
}

public class TourLauncherTests : TestContext
{
    [Fact]
    public void WindowButton_RequestsTheTour()
    {
        var request = new MulticlassRace.Services.Tour.GuidedTourRequest();
        Services.AddSingleton(request);
        var requested = 0;
        request.Requested += () => requested++;

        var cut = RenderComponent<TourLauncher>();
        cut.Find("button.wec-window-btn--tour").Click();

        Assert.Equal(1, requested);
    }
}
