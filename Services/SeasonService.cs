using MulticlassRace.Models;
using MulticlassRace.Repositories.Abstractions;
using MulticlassRace.Services.Abstractions;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Services;

public class SeasonService : ISeasonService
{
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRaceRepository _raceRepository;
    private readonly IChampionshipRepository _championshipRepository;

    public SeasonService(
        ISeasonRepository seasonRepository,
        IRaceRepository raceRepository,
        IChampionshipRepository championshipRepository)
    {
        _seasonRepository = seasonRepository;
        _raceRepository = raceRepository;
        _championshipRepository = championshipRepository;
    }

    public async Task<IEnumerable<SeasonModel>> GetSeasonsByChampionshipAsync(Guid championshipId)
    {
        var seasons = await _seasonRepository.GetSeasonsByChampionshipAsync(championshipId);

        return seasons.Select(s => new SeasonModel
        {
            SeasonId = s.SeasonId,
            SeasonName = s.SeasonName,
            RaceCount = s.Races.Count
        });
    }

    public async Task CreateSeasonAsync(Guid championshipId, string seasonName)
    {
        var championship = await _championshipRepository.GetByIdAsync(championshipId);

        if (championship is null)
        {
            throw new InvalidOperationException("Championship not found.");
        }

        var season = new Season
        {
            SeasonId = Guid.NewGuid(),
            SeasonName = seasonName.Trim(),
            Championship = championship
        };

        await _seasonRepository.AddAsync(season);
    }

    public async Task DeleteSeasonAsync(Guid seasonId)
    {
        await _seasonRepository.DeleteAsync(seasonId);
    }

    public async Task<IEnumerable<DriverStandingsModel>> GetDriverStandingsAsync(Guid seasonId)
    {
        var races = await _raceRepository.GetRacesBySeasonAsync(seasonId);

        if (races is null)
        {
            throw new InvalidOperationException("Season not found.");
        }

        var raceSessions = (await Task.WhenAll(races.Select(async race =>
        {
            var sessions = await _raceRepository.GetSessionsByRaceAsync(race.RaceId);

            if (sessions is null)
            {
                throw new InvalidOperationException("Sessions not found for race.");
            }

            var raceSession = sessions.FirstOrDefault(s => s.SessionType == SessionType.Race);

            if (raceSession is null)
            {
                throw new InvalidOperationException("No race sessions found for race.");
            }

            return raceSession;
        }).ToArray())).ToList();

        if (!raceSessions.Any())
        {
            return new List<DriverStandingsModel>();
        }

        var unionedDrivers = raceSessions
            .SelectMany(s => s.DriverStandings)
            .GroupBy(dr => dr.DriverName)
            .Select(g => g)
            .ToList();

        var driverStandings = unionedDrivers.Select((driverGroup, index) =>
        {
            var driverStanding = driverGroup.First();
            var sumPoints = driverGroup.Sum(d => d.Points);
            return new DriverStandingsModel
            {
                SeasonId = seasonId,
                DriverName = driverStanding.DriverName,
                DriverClass = driverStanding.TeamClassName,
                TeamLogoUrl = driverStanding.Driver.Team?.TeamLogo,
                TeamName = driverStanding.TeamName,
                Points = driverGroup.Sum(d => d.Points)
            };
        }).OrderBy(s => s.DriverClass).ThenByDescending(s => s.Points).ToList();

        return driverStandings;
    }
}