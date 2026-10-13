using MulticlassRace.Common;
using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.ViewModels;

public class RaceFormatTests
{
    [Theory]
    [InlineData(0, "—")]
    [InlineData(-5, "—")]
    [InlineData(83456, "1:23.456")]
    [InlineData(59001, "0:59.001")]
    public void FormatLap_RendersMinutesSecondsMillis(int ms, string expected)
        => Assert.Equal(expected, RaceFormat.FormatLap(ms));

    [Theory]
    [InlineData(0, "0m 00s")]
    [InlineData(125, "2m 05s")]
    [InlineData(3600, "1h 00m")]
    [InlineData(5400, "1h 30m")]
    [InlineData(-10, "0m 00s")]
    public void FormatDuration_UsesHoursOnlyFromOneHour(int seconds, string expected)
        => Assert.Equal(expected, RaceFormat.FormatDuration(seconds));

    [Fact]
    public void TrackImageUrl_EscapesAndAddsLayoutOnlyWhenPresent()
    {
        Assert.Equal("/track-image?track=monza%20gp&type=preview", RaceFormat.TrackImageUrl("monza gp", null, "preview"));
        Assert.Equal("/track-image?track=a&type=outline&layout=b%2Fc", RaceFormat.TrackImageUrl("a", "b/c", "outline"));
        Assert.Equal("/track-image?track=a&type=outline", RaceFormat.TrackImageUrl("a", "  ", "outline"));
    }

    [Fact]
    public void StatusLabelAndClass_CoverEveryStatus()
    {
        Assert.Equal("Not Started", RaceFormat.StatusLabel(RaceStatus.NotStarted));
        Assert.Equal("In Progress", RaceFormat.StatusLabel(RaceStatus.InProgress));
        Assert.Equal("Finished", RaceFormat.StatusLabel(RaceStatus.Finished));
        Assert.Equal("wec-status--inprogress", RaceFormat.StatusClass(RaceStatus.InProgress));
        Assert.Equal("wec-status--finished", RaceFormat.StatusClass(RaceStatus.Finished));
    }
}

public class SessionConditionSetTests
{
    [Fact]
    public void Defaults_AreClearMidDayAndPenalties()
    {
        var set = new SessionConditionSet();

        Assert.Equal(new SessionConditions("3_clear", -16, true), set.Get(SessionType.Race));
        Assert.Equal("Mid-Day (12:00)", SessionConditionOptions.LabelFor(set.Get(SessionType.Race).SunAngle));
    }

    [Fact]
    public void Setters_AffectOnlyTheirSessionType()
    {
        var set = new SessionConditionSet();

        set.SetWeather(SessionType.Practice, "1_heavy_fog");
        set.SetTimeOfDay(SessionType.Practice, "Sunset (18:00)");
        set.SetPenalties(SessionType.Practice, false);

        Assert.Equal(new SessionConditions("1_heavy_fog", 80, false), set.Get(SessionType.Practice));
        Assert.Equal("3_clear", set.Get(SessionType.Qualifying).Weather);
    }

    [Fact]
    public void BlankWeatherOrUnknownTimeFallBackToDefaults()
    {
        var set = new SessionConditionSet();
        set.SetWeather(SessionType.Race, "1_heavy_fog");
        set.SetTimeOfDay(SessionType.Race, "Sunset (18:00)");

        set.SetWeather(SessionType.Race, " ");
        set.SetTimeOfDay(SessionType.Race, "nonsense");

        Assert.Equal("3_clear", set.Get(SessionType.Race).Weather);
        Assert.Equal(-16, set.Get(SessionType.Race).SunAngle);
    }

    [Fact]
    public void Reset_RestoresDefaultsForEverySessionType()
    {
        var set = new SessionConditionSet();
        set.SetPenalties(SessionType.Race, false);

        set.Reset();

        Assert.True(set.Get(SessionType.Race).Penalties);
    }

    [Fact]
    public void TimeOfDayOptions_RoundTripThroughLabels()
    {
        foreach (var (label, angle) in SessionConditionOptions.TimeOfDay)
        {
            Assert.Equal(label, SessionConditionOptions.LabelFor(angle));
        }
    }
}

public class RaceSessionContextTests
{
    private static SessionModel Session(SessionType type, bool withStandings) => new()
    {
        SessionType = type,
        DriverStandings = withStandings
            ? new() { new DriverStandingModel { DriverName = "D", TeamName = "T", TeamClassName = "C" } }
            : new()
    };

    private static RaceSessionContext Context(
        bool teams = true,
        SessionModel[]? sessions = null,
        int? practice = 20,
        string? track = "monza",
        int? pits = 30,
        int cars = 10,
        SessionType selected = SessionType.Qualifying) => new(teams, sessions ?? Array.Empty<SessionModel>(), practice, track, pits, cars, selected);

    [Fact]
    public void Tabs_RequireEnteredTeams()
    {
        var none = Context(teams: false);

        Assert.False(none.CanOpenPractice);
        Assert.False(none.CanOpenQualifying);
        Assert.False(none.CanOpenRace);
    }

    [Fact]
    public void RaceTab_NeedsQualifyingData()
    {
        Assert.False(Context().CanOpenRace);
        Assert.True(Context(sessions: new[] { Session(SessionType.Qualifying, true) }).CanOpenRace);
        Assert.False(Context(sessions: new[] { Session(SessionType.Qualifying, false) }).CanOpenRace);
    }

    [Fact]
    public void PracticeTab_DisabledWhenDurationIsZero()
    {
        Assert.False(Context(practice: 0).CanOpenPractice);
        Assert.True(Context(practice: null).CanOpenPractice);
    }

    [Theory]
    [InlineData(false, SessionType.Race, 0)]
    public void InitialTab_NoTeamsShowsTeams(bool teams, SessionType session, int _)
    {
        var (tab, type) = Context(teams: teams).InitialTab();

        Assert.Equal(RacePopupTab.Teams, tab);
        Assert.Equal(session, type);
    }

    [Fact]
    public void InitialTab_PrefersRaceWhenQualifyingDone_ThenPractice_ThenQualifying()
    {
        Assert.Equal((RacePopupTab.Race, SessionType.Race),
            Context(sessions: new[] { Session(SessionType.Qualifying, true) }).InitialTab());

        Assert.Equal((RacePopupTab.Practice, SessionType.Practice), Context().InitialTab());

        Assert.Equal((RacePopupTab.Qualifying, SessionType.Qualifying),
            Context(sessions: new[] { Session(SessionType.Practice, true) }).InitialTab());

        Assert.Equal((RacePopupTab.Qualifying, SessionType.Qualifying), Context(practice: 0).InitialTab());
    }

    [Fact]
    public void CanStart_RequiresTrackAndPitsAndNoExistingData()
    {
        Assert.True(Context().CanStartSession);
        Assert.False(Context(track: " ").CanStartSession);
        Assert.False(Context(pits: 5, cars: 6).CanStartSession);
        Assert.True(Context(pits: null, cars: 99).CanStartSession);
        Assert.False(Context(sessions: new[] { Session(SessionType.Qualifying, true) }).CanStartSession);
    }

    [Fact]
    public void StartTitle_ExplainsEachBlocker()
    {
        Assert.Contains("Select a track", Context(track: null).StartSessionTitle);
        Assert.Contains("(6) is more than the number of pits (5)", Context(pits: 5, cars: 6).StartSessionTitle);
        Assert.Contains("duration is set to 0", Context(practice: 0, selected: SessionType.Practice).StartSessionTitle);
        Assert.Contains("Enter at least one team", Context(teams: false, selected: SessionType.Qualifying).StartSessionTitle);
        Assert.Contains("Run a qualifying session", Context(selected: SessionType.Race).StartSessionTitle);
        Assert.Equal("A qualifying session already has data.",
            Context(sessions: new[] { Session(SessionType.Qualifying, true) }).StartSessionTitle);
        Assert.Null(Context().StartSessionTitle);
    }
}

public class RaceTeamFilterTests
{
    private static readonly Guid SettingId = Guid.NewGuid();

    private static TeamModel Team(string name, params (Guid setting, string item, string color)[] tags) => new()
    {
        TeamId = Guid.NewGuid(),
        TeamName = name,
        ClassTags = tags.Select(t => new ClassTagModel { ClassSettingId = t.setting, ItemName = t.item, SettingName = "WEC", Color = t.color }).ToList()
    };

    [Fact]
    public void OnlyClassesOfTheRacesSettingCount()
    {
        var team = Team("A", (Guid.NewGuid(), "Other", "#111"), (SettingId, "GT3", "#222"));
        var filter = new RaceTeamFilter(SettingId);

        Assert.Equal("GT3", filter.ClassName(team));
        Assert.Equal("#222", filter.ClassColor(team));
        Assert.Equal("WEC - GT3", filter.ClassLabel(team));
    }

    [Fact]
    public void WithoutSettingEveryTeamIsUncategorizedAndGrey()
    {
        var team = Team("A", (SettingId, "GT3", "#222"));
        var filter = new RaceTeamFilter(null);

        Assert.Equal("", filter.ClassName(team));
        Assert.Equal(RaceFormat.UnknownClassColor, filter.ClassColor(team));
        Assert.True(filter.HasUncategorized(new[] { team }));
    }

    [Fact]
    public void Filter_ByLabelUncategorizedAndAll_SortedByName()
    {
        var zed = Team("Zed", (SettingId, "GT3", "#1"));
        var amy = Team("amy", (SettingId, "GT3", "#1"));
        var none = Team("None");
        var filter = new RaceTeamFilter(SettingId);
        var all = new[] { zed, amy, none };

        Assert.Equal(new[] { amy, none, zed }, filter.Filter(all, ""));
        Assert.Equal(new[] { amy, zed }, filter.Filter(all, "wec - gt3"));
        Assert.Equal(new[] { none }, filter.Filter(all, ClassFilterKeys.Uncategorized));
    }

    [Fact]
    public void ClassLabels_AreDistinctSortedAndSkipBlank()
    {
        var filter = new RaceTeamFilter(SettingId);
        var teams = new[]
        {
            Team("A", (SettingId, "LMP2", "#1")),
            Team("B", (SettingId, "GT3", "#1")),
            Team("C", (SettingId, "GT3", "#1")),
            Team("D")
        };

        Assert.Equal(new[] { "WEC - GT3", "WEC - LMP2" }, filter.ClassLabels(teams));
    }
}

public class ClassColorMapTests
{
    [Fact]
    public void FirstSettingWinsAndLookupIsTrimmedAndCaseInsensitive()
    {
        var map = ClassColorMap.From(new[]
        {
            new ClassSettingModel { ClassSettingName = "A", Items = { new ClassSettingItemModel { Name = "GT3", Color = "#111", Order = 1 } } },
            new ClassSettingModel { ClassSettingName = "B", Items = { new ClassSettingItemModel { Name = "gt3", Color = "#222", Order = 1 } } }
        });

        Assert.Equal("#111", map.ColorFor("  gt3 "));
        Assert.Equal(RaceFormat.UnknownClassColor, map.ColorFor("missing"));
        Assert.Equal(RaceFormat.UnknownClassColor, map.ColorFor(null));
        Assert.Equal(RaceFormat.UnknownClassColor, ClassColorMap.Empty.ColorFor("GT3"));
    }
}

public class StandingsFilterTests
{
    private static DriverStandingsModel Standing(string cls) => new() { DriverName = "D", DriverClass = cls, TeamName = "T" };

    [Fact]
    public void ClassNames_AreDistinctSortedAndNonBlank()
    {
        var names = StandingsFilter.ClassNames(new[] { Standing("LMP2"), Standing("GT3"), Standing("GT3"), Standing("") });

        Assert.Equal(new[] { "GT3", "LMP2" }, names);
    }

    [Fact]
    public void ByClass_EmptyReturnsAllElseCaseInsensitiveMatch()
    {
        var all = new[] { Standing("GT3"), Standing("LMP2") };

        Assert.Equal(2, StandingsFilter.ByClass(all, "").Count);
        Assert.Single(StandingsFilter.ByClass(all, "gt3"));
    }

    [Fact]
    public void SessionClassNames_CollectAcrossSessions()
    {
        var sessions = new[]
        {
            new SessionModel { DriverStandings = { new DriverStandingModel { DriverName = "a", TeamName = "t", TeamClassName = "GT3" } } },
            new SessionModel { DriverStandings = { new DriverStandingModel { DriverName = "b", TeamName = "t", TeamClassName = "LMP2" } } }
        };

        Assert.Equal(new[] { "GT3", "LMP2" }, StandingsFilter.SessionClassNames(sessions));
    }
}

public class RaceFormRulesTests
{
    private static RaceFormModel Form(string name = "R", string? track = "monza", string? layout = null,
        Guid? point = null, Guid? cls = null) => new()
    {
        RaceName = name,
        TrackName = track,
        TrackLayout = layout,
        PointSettingId = point ?? Guid.NewGuid(),
        ClassSettingId = cls ?? Guid.NewGuid()
    };

    private static readonly TrackLayoutOptionModel Gp = new("gp", "GP");
    private static readonly TrackLayoutOptionModel Default = new("", "Default");

    [Fact]
    public void Layout_RequiredOnlyWhenLayoutsExistWithoutDefault()
    {
        Assert.False(RaceFormRules.RequiresLayout(Array.Empty<TrackLayoutOptionModel>()));
        Assert.True(RaceFormRules.RequiresLayout(new[] { Gp }));
        Assert.False(RaceFormRules.RequiresLayout(new[] { Default, Gp }));
        Assert.True(RaceFormRules.HasDefaultLayout(new[] { Default }));
    }

    [Fact]
    public void TrackPreview_NeedsTrackAndResolvedLayout()
    {
        Assert.False(RaceFormRules.ShowTrackPreview(Form(track: null), Array.Empty<TrackLayoutOptionModel>()));
        Assert.True(RaceFormRules.ShowTrackPreview(Form(), Array.Empty<TrackLayoutOptionModel>()));
        Assert.False(RaceFormRules.ShowTrackPreview(Form(), new[] { Gp }));
        Assert.True(RaceFormRules.ShowTrackPreview(Form(layout: "gp"), new[] { Gp }));
        Assert.True(RaceFormRules.ShowTrackPreview(Form(), new[] { Default, Gp }));
    }

    [Fact]
    public void Validate_ReportsFirstProblemInOrder()
    {
        Assert.Equal("Race name is required.", RaceFormRules.Validate(Form(name: " "), Array.Empty<TrackLayoutOptionModel>()));
        Assert.Equal("Select a track.", RaceFormRules.Validate(Form(track: ""), Array.Empty<TrackLayoutOptionModel>()));
        Assert.Equal("Select a track layout.", RaceFormRules.Validate(Form(), new[] { Gp }));
        Assert.Equal("Select a point setting.", RaceFormRules.Validate(Form(point: Guid.Empty), Array.Empty<TrackLayoutOptionModel>()));
        Assert.Equal("Select a class setting.", RaceFormRules.Validate(Form(cls: Guid.Empty), Array.Empty<TrackLayoutOptionModel>()));
        Assert.Null(RaceFormRules.Validate(Form(), Array.Empty<TrackLayoutOptionModel>()));
    }

    [Theory]
    [InlineData(10, 90000, 900)]
    [InlineData(1, 83456, 83)]
    [InlineData(0, 90000, null)]
    [InlineData(10, null, null)]
    [InlineData(null, 90000, null)]
    public void EstimateDuration_NeedsLapsAndBestLap(int? laps, int? lapMs, int? expected)
        => Assert.Equal(expected, RaceFormRules.EstimateDurationSeconds(laps, lapMs));

    [Fact]
    public void Clamps_UseTwentyMinuteDefaultAndLimits()
    {
        Assert.Equal(20, RaceFormRules.ClampPracticeMinutes(null));
        Assert.Equal(0, RaceFormRules.ClampPracticeMinutes(-4));
        Assert.Equal(90, RaceFormRules.ClampPracticeMinutes(500));
        Assert.Equal(5, RaceFormRules.ClampQualifyingMinutes(0));
        Assert.Equal(90, RaceFormRules.ClampQualifyingMinutes(500));
    }
}
