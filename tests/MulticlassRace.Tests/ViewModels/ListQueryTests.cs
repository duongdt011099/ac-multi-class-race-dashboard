using MulticlassRace.Common;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.ViewModels;

internal static class Tags
{
    public static ClassTagModel Tag(string setting, string item, int order = 1) => new()
    {
        ClassSettingItemId = Guid.NewGuid(),
        SettingName = setting,
        ItemName = item,
        Order = order
    };
}

public class TeamListQueryTests
{
    private static TeamModel Team(string name, bool active = true, params ClassTagModel[] tags) => new()
    {
        TeamId = Guid.NewGuid(),
        TeamName = name,
        IsActive = active,
        ClassTags = tags.ToList()
    };

    private static readonly SortState<TeamSortColumn> ByTeam = new(TeamSortColumn.Team);

    [Fact]
    public void Filter_ByClassLabelIsCaseInsensitive()
    {
        var gt3 = Team("A", true, Tags.Tag("WEC", "GT3"));
        var hyper = Team("B", true, Tags.Tag("WEC", "Hypercar"));

        var result = TeamListQuery.Apply(new[] { gt3, hyper }, Array.Empty<DriverModel>(), "wec - gt3", "", ByTeam);

        Assert.Equal(new[] { gt3 }, result);
    }

    [Fact]
    public void Search_MatchesTeamNameOrDriverOfTeam()
    {
        var ferrari = Team("Ferrari");
        var porsche = Team("Porsche");
        var drivers = new[] { new DriverModel { DriverId = Guid.NewGuid(), DriverName = "Alonso", Car = "", CarDisplayName = "", Skin = "", TeamId = porsche.TeamId } };

        Assert.Equal(new[] { ferrari }, TeamListQuery.Apply(new[] { ferrari, porsche }, drivers, "", "ferr", ByTeam));
        Assert.Equal(new[] { porsche }, TeamListQuery.Apply(new[] { ferrari, porsche }, drivers, "", "alon", ByTeam));
    }

    [Fact]
    public void Sort_ByStatusAndDescendingTeam()
    {
        var active = Team("B", true);
        var inactive = Team("A", false);
        var sort = new SortState<TeamSortColumn>(TeamSortColumn.Status);

        Assert.Equal(new[] { inactive, active }, TeamListQuery.Apply(new[] { active, inactive }, Array.Empty<DriverModel>(), "", "", sort));

        sort.Toggle(TeamSortColumn.Team);
        sort.Toggle(TeamSortColumn.Team);
        Assert.Equal(new[] { active, inactive }, TeamListQuery.Apply(new[] { inactive, active }, Array.Empty<DriverModel>(), "", "", sort));
    }

    [Fact]
    public void Sort_ByClassUsesFirstTagAndPutsClasslessFirst()
    {
        var none = Team("None");
        var gt3 = Team("Gt3", true, Tags.Tag("WEC", "GT3"));
        var sort = new SortState<TeamSortColumn>(TeamSortColumn.Class);

        Assert.Equal(new[] { none, gt3 }, TeamListQuery.Apply(new[] { gt3, none }, Array.Empty<DriverModel>(), "", "", sort));
    }

    [Fact]
    public void ClassNames_AreDistinctAndSorted()
    {
        var teams = new[]
        {
            Team("A", true, Tags.Tag("WEC", "LMP2")),
            Team("B", true, Tags.Tag("WEC", "GT3"), Tags.Tag("wec", "lmp2"))
        };

        Assert.Equal(new[] { "WEC - GT3", "WEC - LMP2" }, TeamListQuery.ClassNames(teams));
    }
}

public class DriverListQueryTests
{
    private static DriverModel Driver(string name, string? team = null, bool active = true, params ClassTagModel[] tags) => new()
    {
        DriverId = Guid.NewGuid(),
        DriverName = name,
        Car = "",
        CarDisplayName = "",
        Skin = "",
        TeamName = team,
        IsActive = active,
        ClassTags = tags.ToList()
    };

    private static readonly SortState<DriverSortColumn> ByDriver = new(DriverSortColumn.Driver);

    [Fact]
    public void Filter_UncategorizedKeepsOnlyClasslessDrivers()
    {
        var plain = Driver("Plain");
        var tagged = Driver("Tagged", tags: Tags.Tag("WEC", "GT3"));

        var result = DriverListQuery.Apply(new[] { plain, tagged }, DriverListQuery.UncategorizedKey, "", ByDriver);

        Assert.Equal(new[] { plain }, result);
        Assert.True(DriverListQuery.HasUncategorized(new[] { plain, tagged }));
        Assert.False(DriverListQuery.HasUncategorized(new[] { tagged }));
    }

    [Fact]
    public void Search_MatchesDriverOrTeamName()
    {
        var a = Driver("Alonso", "Ferrari");
        var b = Driver("Button", "McLaren");

        Assert.Equal(new[] { a }, DriverListQuery.Apply(new[] { a, b }, "", "ferr", ByDriver));
        Assert.Equal(new[] { b }, DriverListQuery.Apply(new[] { a, b }, "", "butt", ByDriver));
    }

    [Fact]
    public void Sort_ByDriverDescending()
    {
        var a = Driver("A");
        var b = Driver("B");
        var sort = new SortState<DriverSortColumn>(DriverSortColumn.Driver, descending: true);

        Assert.Equal(new[] { b, a }, DriverListQuery.Apply(new[] { a, b }, "", "", sort));
    }
}

public class ClassItemOptionTests
{
    private static ClassItemOption Option(string setting, string name, int order) => new()
    {
        Id = Guid.NewGuid(),
        SettingName = setting,
        Name = name,
        Color = "#000",
        Order = order
    };

    [Fact]
    public void Available_ExcludesSelectedAndRestrictsToSameOrder()
    {
        var hyper = Option("WEC", "Hypercar", 1);
        var lmp2 = Option("WEC", "LMP2", 2);
        var imsaTop = Option("IMSA", "GTP", 1);
        var all = new[] { hyper, lmp2, imsaTop };

        var result = ClassItemOption.Available(all, new[] { hyper.Id }, null);

        Assert.Equal(new[] { imsaTop }, result);
    }

    [Fact]
    public void Available_WithNothingSelectedOffersEverythingFiltered()
    {
        var hyper = Option("WEC", "Hypercar", 1);
        var lmp2 = Option("WEC", "LMP2", 2);

        var result = ClassItemOption.Available(new[] { hyper, lmp2 }, Array.Empty<Guid>(), "lmp");

        Assert.Equal(new[] { lmp2 }, result);
    }

    [Fact]
    public void Selected_KeepsSelectionOrderAndSkipsUnknown()
    {
        var a = Option("WEC", "A", 1);
        var b = Option("WEC", "B", 1);

        var result = ClassItemOption.Selected(new[] { a, b }, new[] { b.Id, Guid.NewGuid(), a.Id });

        Assert.Equal(new[] { b, a }, result);
    }

    [Fact]
    public void FromSettings_FlattensAndSortsByLabel()
    {
        var settings = new[]
        {
            new ClassSettingModel
            {
                ClassSettingId = Guid.NewGuid(), ClassSettingName = "WEC",
                Items = { new ClassSettingItemModel { Name = "LMP2", Color = "#1", Order = 2 }, new ClassSettingItemModel { Name = "GT3", Color = "#2", Order = 3 } }
            }
        };

        Assert.Equal(new[] { "WEC - GT3", "WEC - LMP2" }, ClassItemOption.FromSettings(settings).Select(o => o.Label));
    }
}
