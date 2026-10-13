using MulticlassRace.Mapping;
using MulticlassRace.Models;

namespace MulticlassRace.Tests.Mapping;

public class MappingTests
{
    private static ClassSetting Setting(string name) => new() { ClassSettingId = Guid.NewGuid(), ClassSettingName = name };

    private static ClassSettingItem Item(ClassSetting setting, string name, int order, string color = "#111") => new()
    {
        ClassSettingItemId = Guid.NewGuid(),
        ClassSettingId = setting.ClassSettingId,
        ClassSetting = setting,
        Name = name,
        Order = order,
        Color = color
    };

    private static Driver Driver(string name, Team? team = null) => new()
    {
        DriverId = Guid.NewGuid(),
        DriverName = name,
        CarDisplayName = "Display",
        Car = "ferrari 499p",
        Skin = "red/1",
        Nationality = "Italy",
        Age = 30,
        DriverStrength = 90,
        DriverAgression = 40,
        IsHuman = true,
        Team = team
    };

    [Fact]
    public void ClassTags_AreOrderedBySettingNameThenRank()
    {
        var wec = Setting("WEC");
        var imsa = Setting("imsa");
        var items = new[] { Item(wec, "GT3", 3), Item(wec, "Hypercar", 1), Item(imsa, "GTP", 1) };

        var tags = items.ToClassTags();

        Assert.Equal(new[] { "GTP", "Hypercar", "GT3" }, tags.Select(t => t.ItemName));
        Assert.Equal("imsa", tags[0].SettingName);
        Assert.Equal(wec.ClassSettingId, tags[1].ClassSettingId);
    }

    [Fact]
    public void ClassSettingModel_OrdersItemsByRank()
    {
        var setting = Setting("WEC");
        setting.IsDefault = true;
        setting.Items = new List<ClassSettingItem> { Item(setting, "B", 2, "#222"), Item(setting, "A", 1, "#111") };

        var model = setting.ToModel();

        Assert.True(model.IsDefault);
        Assert.Equal(new[] { "A", "B" }, model.Items.Select(i => i.Name));
        Assert.Equal("#111", model.Items[0].Color);
    }

    [Fact]
    public void PointSettingModel_CopiesConfig()
    {
        var model = new PointSetting { SettingId = Guid.NewGuid(), SettingName = "S", IsDefault = true, Config = new() { ["1"] = 25 } }.ToModel();

        Assert.Equal(25, model.Config["1"]);
        Assert.True(model.IsDefault);
    }

    [Fact]
    public void TeamModel_OnlyIncludesDriversWhenAsked()
    {
        var setting = Setting("WEC");
        var team = new Team { TeamId = Guid.NewGuid(), TeamName = "Ferrari", TeamLogo = "/l.png", IsActive = false };
        team.ClassSettingItems.Add(Item(setting, "Hypercar", 1));
        team.Drivers.Add(Driver("Alonso", team));

        var without = team.ToModel();
        var with = team.ToModel(includeDrivers: true);

        Assert.Empty(without.Drivers);
        Assert.Equal("Alonso", Assert.Single(with.Drivers).DriverName);
        Assert.Equal("/l.png", with.TeamLogo);
        Assert.False(with.IsActive);
        Assert.Equal("Hypercar", Assert.Single(with.ClassTags).ItemName);
    }

    [Fact]
    public void DriverModel_BuildsImageLinksOnlyWithGamePath()
    {
        var driver = Driver("Alonso");

        var withGame = driver.ToModel(@"C:\Games\AC");
        var withoutGame = driver.ToModel(null);

        Assert.Equal("/car-image?car=ferrari%20499p&skin=red%2F1&type=livery", withGame.LiveryImageUrl);
        Assert.Equal("/car-image?car=ferrari%20499p&skin=red%2F1&type=car", withGame.CarImageUrl);
        Assert.Null(withoutGame.LiveryImageUrl);
        Assert.Null(DriverMappings.BuildImageUrl("x", "", "skin", "car"));
        Assert.Null(DriverMappings.BuildImageUrl("x", "car", " ", "car"));
    }

    [Fact]
    public void DriverModel_CarriesTeamAndClassTags()
    {
        var setting = Setting("WEC");
        var team = new Team { TeamId = Guid.NewGuid(), TeamName = "Ferrari", TeamLogo = "/l.png" };
        team.ClassSettingItems.Add(Item(setting, "Hypercar", 1));
        var model = Driver("Alonso", team).ToModel("game");

        Assert.Equal("Ferrari", model.TeamName);
        Assert.Equal(team.TeamId, model.TeamId);
        Assert.Equal("/l.png", model.TeamLogo);
        Assert.Equal("Hypercar", Assert.Single(model.ClassTags).ItemName);
        Assert.True(model.IsHuman);
        Assert.Equal(90, model.DriverStrength);
    }

    [Fact]
    public void DriverModel_WithoutTeamHasNoTags()
    {
        var model = Driver("Free").ToModel("game");

        Assert.Null(model.TeamName);
        Assert.Empty(model.ClassTags);
    }

    [Fact]
    public void DriverSummary_HasNoImagesOrTags()
    {
        var team = new Team { TeamId = Guid.NewGuid(), TeamName = "T", TeamLogo = "/l.png" };
        var summary = Driver("Alonso", team).ToSummaryModel();

        Assert.Null(summary.CarImageUrl);
        Assert.Empty(summary.ClassTags);
        Assert.Equal(team.TeamId, summary.TeamId);
        Assert.Equal("/l.png", summary.TeamLogo);
    }

    [Fact]
    public void DriverForm_CopiesEditableFields()
    {
        var team = new Team { TeamId = Guid.NewGuid(), TeamName = "T" };
        var form = Driver("Alonso", team).ToFormModel();

        Assert.Equal("Alonso", form.DriverName);
        Assert.Equal(30, form.Age);
        Assert.Equal(40, form.DriverAgression);
        Assert.Equal(team.TeamId, form.TeamId);
        Assert.Equal("T", form.TeamName);
        Assert.True(form.IsHuman);
    }

    [Fact]
    public void RaceModel_CountsStandingsAcrossSessions()
    {
        var season = new Season { SeasonId = Guid.NewGuid(), SeasonName = "S", Championship = new Championship { ChampionshipId = Guid.NewGuid(), ChampionshipName = "C" } };
        var point = new PointSetting { SettingId = Guid.NewGuid(), SettingName = "Standard", IsDefault = true, Config = new() };
        var race = new Models.Race
        {
            RaceId = Guid.NewGuid(),
            RaceName = "Monza",
            Status = RaceStatus.InProgress,
            PointSetting = point,
            PointSettingId = point.SettingId,
            Season = season,
            Sessions = new List<Session>(),
            EnteredTeams = new List<Team>()
        };
        race.Sessions.Add(new Session
        {
            RaceId = race.RaceId,
            Race = race,
            DriverStandings = new List<DriverStanding>
            {
                new() { Driver = Driver("a"), DriverName = "a", OriginalTeamId = Guid.NewGuid(), TeamName = "t", TeamClassName = "c", Race = race },
                new() { Driver = Driver("b"), DriverName = "b", OriginalTeamId = Guid.NewGuid(), TeamName = "t", TeamClassName = "c", Race = race }
            }
        });

        var model = race.ToModel();

        Assert.Equal(2, model.StandingCount);
        Assert.Equal("Standard", model.PointSettingName);
        Assert.Equal(string.Empty, model.ClassSettingName);
        Assert.Equal(RaceStatus.InProgress, model.Status);
    }

    [Fact]
    public void ChampionshipAndSeasonModels_CountChildren()
    {
        var championship = new Championship { ChampionshipId = Guid.NewGuid(), ChampionshipName = "C", IsActive = false };
        var season = new Season { SeasonId = Guid.NewGuid(), SeasonName = "S", Championship = championship };
        championship.Seasons.Add(season);

        Assert.Equal(1, championship.ToModel().SeasonCount);
        Assert.False(championship.ToModel().IsActive);
        Assert.Equal("C", championship.ToFormModel().ChampionshipName);
        Assert.Equal(0, season.ToModel().RaceCount);
    }
}
