using Bunit;
using multi_class_race_dashboard.Components.Features.Races;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.Components;

public class RaceComponentTests : TestContext
{
    private static DriverStandingsModel Standing(string driver, string cls, int points, string team = "Team") => new()
    {
        DriverName = driver,
        DriverClass = cls,
        Points = points,
        TeamName = team
    };

    private static ClassColorMap Colors() => ClassColorMap.From(new[]
    {
        new ClassSettingModel
        {
            ClassSettingName = "WEC",
            Items = { new ClassSettingItemModel { Name = "Hypercar", Color = "#e10600", Order = 1 } }
        }
    });

    [Fact]
    public void StandingsModal_WhileLoading_ShowsLoadingText()
    {
        var cut = RenderComponent<RaceStandingsModal>();

        Assert.Equal("Loading standings...", cut.Find(".wec-empty").TextContent);
    }

    [Fact]
    public void StandingsModal_NoStandings_ShowsEmptyText()
    {
        var cut = RenderComponent<RaceStandingsModal>(p => p.Add(m => m.Standings, new List<DriverStandingsModel>()));

        Assert.Contains("No driver standings", cut.Find(".wec-empty").TextContent);
    }

    [Fact]
    public void StandingsModal_PodiumOrdersSecondFirstThird_AndRestGoToTable()
    {
        var standings = new List<DriverStandingsModel>
        {
            Standing("One", "Hypercar", 100), Standing("Two", "Hypercar", 80), Standing("Three", "Hypercar", 60),
            Standing("Four", "Hypercar", 40), Standing("Five", "Hypercar", 20)
        };

        var cut = RenderComponent<RaceStandingsModal>(p => p
            .Add(m => m.Standings, standings)
            .Add(m => m.Colors, Colors()));

        Assert.Equal(new[] { "Two", "One", "Three" }, cut.FindAll(".wec-podium__name").Select(n => n.TextContent));
        Assert.Equal(new[] { "2", "1", "3" }, cut.FindAll(".wec-podium__pos").Select(n => n.TextContent));
        Assert.Equal(new[] { "4", "5" }, cut.FindAll("tbody .wec-table__position").Select(n => n.TextContent));
        Assert.Contains("#e10600", cut.Find(".wec-badge").GetAttribute("style"));
    }

    [Fact]
    public void StandingsModal_ClassFilterNarrowsPodium()
    {
        var standings = new List<DriverStandingsModel>
        {
            Standing("Hyper", "Hypercar", 100), Standing("Gt", "GT3", 90)
        };
        var cut = RenderComponent<RaceStandingsModal>(p => p.Add(m => m.Standings, standings));

        cut.Find("#standing-class-filter").Change("GT3");

        Assert.Equal(new[] { "Gt" }, cut.FindAll(".wec-podium__name").Select(n => n.TextContent));
    }

    [Fact]
    public void StandingsModal_PodiumFallsBackToTeamInitials()
    {
        var cut = RenderComponent<RaceStandingsModal>(p => p
            .Add(m => m.Standings, new List<DriverStandingsModel> { Standing("One", "Hypercar", 10, "Ferrari") }));

        Assert.Equal("FE", cut.Find(".wec-podium__logo--fallback").TextContent.Trim());
    }
}
