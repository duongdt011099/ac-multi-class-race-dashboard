using System.Text.Json;
using MulticlassRace.Models;
using MulticlassRace.ViewModels;
using MulticlassRace.Builders;
using Xunit;

namespace MulticlassRace.Tests.ViewModels;

public class SessionManifestTests
{
    [Fact]
    public void Build_ListsClassesInOrderAndCarsByGridIndex()
    {
        var setting = new ClassSetting { ClassSettingId = Guid.NewGuid(), ClassSettingName = "S" };
        var gt3 = new ClassSettingItem { ClassSettingItemId = Guid.NewGuid(), ClassSettingId = setting.ClassSettingId, Name = "GT3", Order = 2, Color = "#111" };
        var hyper = new ClassSettingItem { ClassSettingItemId = Guid.NewGuid(), ClassSettingId = setting.ClassSettingId, Name = "Hyper", Order = 1, Color = "#222" };
        setting.Items.Add(gt3);
        setting.Items.Add(hyper);

        var race = new Race { RaceId = Guid.NewGuid(), RaceName = "R", ClassSettingId = setting.ClassSettingId, ClassSetting = setting, Status = RaceStatus.NotStarted, Season = null!, PointSetting = null!, Sessions = new List<Session>(), EnteredTeams = new List<Team>() };
        var teamA = new Team { TeamName = "A" };
        teamA.ClassSettingItems.Add(hyper);
        var teamB = new Team { TeamName = "B" };
        teamB.ClassSettingItems.Add(gt3);

        var human = new Driver { DriverName = "Me", CarDisplayName = "Me", Car = "car_hyper", Skin = "s", Team = teamA };
        var ai = new Driver { DriverName = "Bot", CarDisplayName = "Bot", Car = "car_gt3", Skin = "s", Team = teamB };

        using var doc = JsonDocument.Parse(SessionManifestBuilder.Build(race, SessionType.Race, human, new[] { ai }));
        var root = doc.RootElement;

        Assert.Equal("Hyper", root.GetProperty("classes")[0].GetProperty("name").GetString());
        Assert.Equal("GT3", root.GetProperty("classes")[1].GetProperty("name").GetString());

        var cars = root.GetProperty("cars");
        Assert.Equal(0, cars[0].GetProperty("index").GetInt32());
        Assert.Equal("Hyper", cars[0].GetProperty("class").GetString());
        Assert.Equal(1, cars[1].GetProperty("index").GetInt32());
        Assert.Equal("car_gt3", cars[1].GetProperty("carId").GetString());
        Assert.Equal("GT3", cars[1].GetProperty("class").GetString());
    }

    [Fact]
    public void SessionCannotStartWithoutSteam()
    {
        var context = new RaceSessionContext(true, null, 20, "monza", null, 2, SessionType.Qualifying, SteamRunning: false);

        Assert.False(context.CanStartSession);
        Assert.Contains("Steam", context.StartSessionTitle);
        Assert.True(context with { SteamRunning = true } is { CanStartSession: true });
    }
}
