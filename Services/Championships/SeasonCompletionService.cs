using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services.Championships;

public class SeasonCompletionService : ISeasonCompletionService
{
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRaceRepository _raceRepository;
    private readonly IAchievementRepository _achievementRepository;
    private readonly SeasonCompletionNotifier _notifier;

    public SeasonCompletionService(
        ISeasonRepository seasonRepository,
        IRaceRepository raceRepository,
        IAchievementRepository achievementRepository,
        SeasonCompletionNotifier notifier)
    {
        _seasonRepository = seasonRepository;
        _raceRepository = raceRepository;
        _achievementRepository = achievementRepository;
        _notifier = notifier;
    }

    public async Task<SeasonCompletion?> OnRaceFinishedAsync(Guid raceId)
    {
        var race = await _raceRepository.GetRaceWithClassSettingAsync(raceId);

        if (race is null)
        {
            return null;
        }

        var season = await _seasonRepository.GetSeasonForCompletionAsync(race.Season.SeasonId);

        if (season is null)
        {
            return null;
        }

        await SyncRaceWinAsync(season, season.Races.First(r => r.RaceId == raceId));

        return await FinishIfDoneAsync(season);
    }

    public async Task<SeasonCompletion?> TryFinishSeasonAsync(Guid seasonId)
    {
        var season = await _seasonRepository.GetSeasonForCompletionAsync(seasonId);

        return season is null ? null : await FinishIfDoneAsync(season);
    }

    private async Task SyncRaceWinAsync(Season season, Race race)
    {
        // A re-imported result replaces the old one, so the trophy follows whoever won this time.
        var existing = (await _achievementRepository.GetByRaceAsync(race.RaceId)).ToList();

        var raceSession = race.Sessions
            .Where(s => s.SessionType == SessionType.Race)
            .OrderByDescending(s => s.SessionDate)
            .FirstOrDefault();

        var win = raceSession?.DriverStandings.FirstOrDefault(s => s.Driver.IsHuman && s.ClassPosition == 1);

        if (existing.Count > 0)
        {
            await _achievementRepository.RemoveRangeAsync(existing);
        }

        if (win is null)
        {
            return;
        }

        await _achievementRepository.AddAsync(new Achievement
        {
            AchievementId = Guid.NewGuid(),
            Type = AchievementType.RaceWin,
            DriverId = win.Driver.DriverId,
            DriverName = win.DriverName,
            SeasonId = season.SeasonId,
            RaceId = race.RaceId,
            ChampionshipName = season.Championship.ChampionshipName,
            SeasonName = season.SeasonName,
            RaceName = race.RaceName,
            TrackName = race.TrackName,
            ClassName = win.TeamClassName,
            Points = win.Points,
            Position = 1,
            EarnedAt = DateTime.Now
        });
    }

    private async Task<SeasonCompletion?> FinishIfDoneAsync(Season season)
    {
        if (season.IsFinished)
        {
            return null;
        }

        var finishedRaces = season.Races.Count(r => r.Status == RaceStatus.Finished);

        if (season.NumberOfRounds is not { } rounds || finishedRaces < rounds)
        {
            return null;
        }

        var standings = season.Races
            .SelectMany(r => r.Sessions)
            .Where(s => s.SessionType == SessionType.Race)
            .SelectMany(s => s.DriverStandings)
            .ToList();

        var humanStandings = standings.Where(s => s.Driver.IsHuman).ToList();

        // The human driver is judged in the class they raced in most.
        var className = humanStandings
            .GroupBy(s => s.TeamClassName)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        var table = className is null
            ? new List<SeasonStandingRow>()
            : BuildTable(standings, className);

        var human = table.FirstOrDefault(r => r.IsHuman);

        season.IsFinished = true;
        season.FinishedAt = DateTime.Now;
        await _seasonRepository.UpdateAsync(season);

        var stale = (await _achievementRepository.GetBySeasonAsync(season.SeasonId, seasonTrophiesOnly: true)).ToList();

        if (stale.Count > 0)
        {
            await _achievementRepository.RemoveRangeAsync(stale);
        }

        if (human is { Position: <= 3 })
        {
            var driver = humanStandings.Last().Driver;

            await _achievementRepository.AddAsync(new Achievement
            {
                AchievementId = Guid.NewGuid(),
                Type = human.Position switch
                {
                    1 => AchievementType.SeasonChampion,
                    2 => AchievementType.SeasonRunnerUp,
                    _ => AchievementType.SeasonThird
                },
                DriverId = driver.DriverId,
                DriverName = human.DriverName,
                SeasonId = season.SeasonId,
                ChampionshipName = season.Championship.ChampionshipName,
                SeasonName = season.SeasonName,
                ClassName = className,
                Points = human.Points,
                Position = human.Position,
                EarnedAt = season.FinishedAt.Value
            });
        }

        var completion = new SeasonCompletion(
            season.SeasonId,
            season.SeasonName,
            season.Championship.ChampionshipName,
            human?.DriverName,
            className,
            human?.Position,
            human?.Points ?? 0,
            table);

        _notifier.NotifyCompleted(completion);

        return completion;
    }

    /// <summary>The season table of one class: points first, then race wins, then name.</summary>
    private static List<SeasonStandingRow> BuildTable(IEnumerable<DriverStanding> standings, string className)
    {
        return standings
            .Where(s => string.Equals(s.TeamClassName, className, StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.DriverName)
            .Select(g => new
            {
                DriverName = g.Key,
                TeamName = g.Last().TeamName,
                Points = g.Sum(s => s.Points),
                Wins = g.Count(s => s.ClassPosition == 1),
                IsHuman = g.Any(s => s.Driver.IsHuman)
            })
            .OrderByDescending(d => d.Points)
            .ThenByDescending(d => d.Wins)
            .ThenBy(d => d.DriverName, StringComparer.OrdinalIgnoreCase)
            .Select((d, index) => new SeasonStandingRow(index + 1, d.DriverName, d.TeamName, className, d.Points, d.IsHuman))
            .ToList();
    }
}
