using Microsoft.EntityFrameworkCore;
using MulticlassRace.Models;
using MulticlassRace.Repositories;
using MulticlassRace.Services.Championships;
using MulticlassRace.Tests.Support;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Tests.Services;

/// <summary>Season rounds, finishing a season, the human driver's trophies and the read-only rule.</summary>
public class SeasonCompletionTests : IDisposable
{
    private readonly RaceServiceFixture _fx = new();
    private readonly Team _humanTeam;
    private readonly Team _aiTeam;
    private readonly Driver _human;
    private readonly Driver _ai;

    public SeasonCompletionTests()
    {
        _humanTeam = _fx.AddTeam("Human Racing", "Hypercar", ("Hugo Human", "car_a", "skin_a", true));
        _aiTeam = _fx.AddTeam("AI Racing", "Hypercar", ("Ada AI", "car_b", "skin_b", false));
        _human = _humanTeam.Drivers.Single();
        _ai = _aiTeam.Drivers.Single();
    }

    public void Dispose() => _fx.Dispose();

    private Season SeasonWithRounds(int rounds)
    {
        var season = _fx.AddSeason();
        season.NumberOfRounds = rounds;
        _fx.Context.SaveChanges();
        return season;
    }

    /// <summary>Records a race result (human first or second) and marks the race finished, as an import does.</summary>
    private Race FinishRace(Season season, string name, bool humanWins)
    {
        var race = _fx.AddRace(season, name, status: RaceStatus.Finished);

        _fx.AddSession(
            race,
            SessionType.Race,
            (_human, _humanTeam, humanWins ? 1 : 2),
            (_ai, _aiTeam, humanWins ? 2 : 1));

        foreach (var standing in _fx.Context.DriverStandings.Where(s => s.Race.RaceId == race.RaceId).Include(s => s.Driver))
        {
            var isHuman = standing.Driver.DriverId == _human.DriverId;
            standing.Points = isHuman == humanWins ? 25 : 18;
        }

        _fx.Context.SaveChanges();
        return race;
    }

    private List<Achievement> Trophies() => _fx.Context.Achievements.AsNoTracking().ToList();

    [Fact]
    public async Task RaceWin_IsRecordedForTheHumanDriver_AndWithdrawnWhenResultChanges()
    {
        var season = SeasonWithRounds(8);
        var race = FinishRace(season, "Monza", humanWins: true);

        var completion = await _fx.SeasonCompletion.OnRaceFinishedAsync(race.RaceId);

        Assert.Null(completion);
        var win = Assert.Single(Trophies());
        Assert.Equal(AchievementType.RaceWin, win.Type);
        Assert.Equal(_human.DriverId, win.DriverId);
        Assert.Equal("Monza", win.RaceName);
        Assert.Equal(race.RaceId, win.RaceId);

        // The result is imported again and the AI wins this time: the trophy goes away.
        var session = _fx.Context.Set<Session>().Include(s => s.DriverStandings).ThenInclude(d => d.Driver).Single();
        foreach (var standing in session.DriverStandings)
        {
            standing.ClassPosition = standing.Driver.IsHuman ? 2 : 1;
        }

        _fx.Context.SaveChanges();
        await _fx.SeasonCompletion.OnRaceFinishedAsync(race.RaceId);

        Assert.Empty(Trophies());
    }

    [Fact]
    public async Task AiWin_GivesNoTrophy()
    {
        var season = SeasonWithRounds(8);
        var race = FinishRace(season, "Monza", humanWins: false);

        await _fx.SeasonCompletion.OnRaceFinishedAsync(race.RaceId);

        Assert.Empty(Trophies());
    }

    [Fact]
    public async Task SeasonStaysOpenUntilEveryRoundIsFinished()
    {
        var season = SeasonWithRounds(2);
        var race = FinishRace(season, "Round 1", humanWins: true);

        var completion = await _fx.SeasonCompletion.OnRaceFinishedAsync(race.RaceId);

        Assert.Null(completion);
        Assert.False((await _fx.Context.Seasons.AsNoTracking().SingleAsync()).IsFinished);
    }

    [Fact]
    public async Task LastRound_CrownsTheHumanWorldChampion_AndAnnouncesIt()
    {
        var season = SeasonWithRounds(2);
        SeasonCompletion? announced = null;
        _fx.SeasonCompletionNotifier.Completed += c => announced = c;

        await _fx.SeasonCompletion.OnRaceFinishedAsync(FinishRace(season, "Round 1", humanWins: true).RaceId);
        var completion = await _fx.SeasonCompletion.OnRaceFinishedAsync(FinishRace(season, "Round 2", humanWins: true).RaceId);

        Assert.NotNull(completion);
        Assert.True(completion.IsWorldChampion);
        Assert.Equal(50, completion.HumanPoints);
        Assert.Equal("Hugo Human", completion.Podium[0].DriverName);
        Assert.Same(completion, announced);

        var saved = await _fx.Context.Seasons.AsNoTracking().SingleAsync();
        Assert.True(saved.IsFinished);
        Assert.NotNull(saved.FinishedAt);

        Assert.Equal(2, Trophies().Count(t => t.Type == AchievementType.RaceWin));
        var title = Assert.Single(Trophies(), t => t.Type == AchievementType.SeasonChampion);
        Assert.Equal(50, title.Points);
        Assert.Equal("Hypercar", title.ClassName);
    }

    [Fact]
    public async Task SecondInTheStandings_IsRunnerUp_NotChampion()
    {
        var season = SeasonWithRounds(2);

        await _fx.SeasonCompletion.OnRaceFinishedAsync(FinishRace(season, "Round 1", humanWins: false).RaceId);
        var completion = await _fx.SeasonCompletion.OnRaceFinishedAsync(FinishRace(season, "Round 2", humanWins: false).RaceId);

        Assert.NotNull(completion);
        Assert.False(completion.IsWorldChampion);
        Assert.Equal(2, completion.HumanPosition);
        Assert.Contains(Trophies(), t => t.Type == AchievementType.SeasonRunnerUp);
        Assert.DoesNotContain(Trophies(), t => t.Type == AchievementType.SeasonChampion);
    }

    [Fact]
    public async Task FinishedSeason_RejectsEveryChangeToItsRaces()
    {
        var season = SeasonWithRounds(1);
        var race = FinishRace(season, "Only round", humanWins: true);
        await _fx.SeasonCompletion.OnRaceFinishedAsync(race.RaceId);

        var form = new RaceFormModel
        {
            RaceId = race.RaceId,
            RaceName = "Changed",
            SeasonId = season.SeasonId,
            PointSettingId = _fx.Context.PointSettings.First().SettingId,
            ClassSettingId = RaceServiceFixture.WecSettingId,
            TrackName = "monza"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Race.CreateRaceAsync(form));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Race.UpdateRaceAsync(form));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Race.DeleteRaceAsync(race.RaceId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Race.SaveRaceTeamsAsync(race.RaceId, new[] { _humanTeam.TeamId }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.SessionLauncher.LaunchSessionAsync(race.RaceId, SessionType.Race, "Clear", 0));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fx.Results.ImportSessionResultAsync(race.RaceId, SessionType.Qualifying, new MemoryStream(), "q.json"));

        Assert.Equal("Only round", (await _fx.Context.Races.AsNoTracking().SingleAsync()).RaceName);
    }

    [Fact]
    public async Task FinishedSeason_CannotBeDeletedOrGetMoreRounds()
    {
        var season = SeasonWithRounds(1);
        await _fx.SeasonCompletion.OnRaceFinishedAsync(FinishRace(season, "Only round", humanWins: true).RaceId);
        var seasons = NewSeasonService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => seasons.DeleteSeasonAsync(season.SeasonId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => seasons.SetSeasonRoundsAsync(season.SeasonId, 5));
        Assert.True(await _fx.Context.Seasons.AnyAsync());
    }

    [Fact]
    public async Task AddingAnotherRaceBeyondTheRounds_IsRejected()
    {
        var season = SeasonWithRounds(1);
        _fx.AddRace(season, "Round 1");

        var form = new RaceFormModel
        {
            RaceName = "Round 2",
            SeasonId = season.SeasonId,
            PointSettingId = _fx.Context.PointSettings.First().SettingId,
            ClassSettingId = RaceServiceFixture.WecSettingId,
            TrackName = "monza"
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Race.CreateRaceAsync(form));
        Assert.Contains("1 rounds", error.Message);
    }

    [Fact]
    public async Task LoweringRoundsToWhatIsAlreadyFinished_EndsTheSeason()
    {
        var season = SeasonWithRounds(8);
        await _fx.SeasonCompletion.OnRaceFinishedAsync(FinishRace(season, "Round 1", humanWins: true).RaceId);
        var seasons = NewSeasonService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => seasons.SetSeasonRoundsAsync(season.SeasonId, 0));

        await seasons.SetSeasonRoundsAsync(season.SeasonId, 1);

        Assert.True((await _fx.Context.Seasons.AsNoTracking().SingleAsync()).IsFinished);
        Assert.Contains(Trophies(), t => t.Type == AchievementType.SeasonChampion);
    }

    [Fact]
    public async Task DeletingAnUnfinishedSeason_RemovesItsTrophies()
    {
        var season = SeasonWithRounds(8);
        await _fx.SeasonCompletion.OnRaceFinishedAsync(FinishRace(season, "Round 1", humanWins: true).RaceId);
        Assert.Single(Trophies());

        await NewSeasonService().DeleteSeasonAsync(season.SeasonId);

        Assert.Empty(Trophies());
    }

    [Fact]
    public async Task FinishedSeasonRaces_AreNotOfferedAsImportTargets()
    {
        var season = SeasonWithRounds(1);
        var race = FinishRace(season, "Only round", humanWins: true);
        await _fx.SeasonCompletion.OnRaceFinishedAsync(race.RaceId);

        var candidates = await _fx.Race.GetCandidateRacesAsync(race.TrackName, SessionType.Race);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task AchievementService_ReturnsOnlyThatDriversTrophies_SeasonHonoursFirst()
    {
        var season = SeasonWithRounds(1);
        await _fx.SeasonCompletion.OnRaceFinishedAsync(FinishRace(season, "Only round", humanWins: true).RaceId);

        var service = new AchievementService(new AchievementRepository(_fx.Context));

        var mine = await service.GetForDriverAsync(_human.DriverId);
        var theirs = await service.GetForDriverAsync(_ai.DriverId);

        Assert.Equal(new[] { AchievementType.SeasonChampion, AchievementType.RaceWin }, mine.Select(a => a.Type));
        Assert.Equal(TrophyTier.Diamond, mine[0].Tier);
        Assert.Equal(TrophyTier.Gold, mine[1].Tier);
        Assert.Empty(theirs);
    }

    [Fact]
    public async Task SeasonWithoutRounds_CannotGetRaces_UntilConfigured_AndNeverFinishesOnItsOwn()
    {
        var season = SeasonWithRounds(8);
        season.NumberOfRounds = null;
        _fx.Context.SaveChanges();

        var form = new RaceFormModel
        {
            RaceName = "Round 1",
            SeasonId = season.SeasonId,
            PointSettingId = _fx.Context.PointSettings.First().SettingId,
            ClassSettingId = RaceServiceFixture.WecSettingId,
            TrackName = "monza"
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _fx.Race.CreateRaceAsync(form));
        Assert.Contains("no number of rounds", error.Message);

        var completion = await _fx.SeasonCompletion.OnRaceFinishedAsync(FinishRace(season, "Old race", humanWins: true).RaceId);
        Assert.Null(completion);

        await NewSeasonService().SetSeasonRoundsAsync(season.SeasonId, 4);
        await _fx.Race.CreateRaceAsync(form);
        Assert.Equal(2, await _fx.Context.Races.CountAsync());
    }

    private SeasonService NewSeasonService() => new(
        new SeasonRepository(_fx.Context),
        new RaceRepository(_fx.Context),
        new ChampionshipRepository(_fx.Context, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChampionshipRepository>.Instance),
        _fx.SeasonCompletion);
}
