using MulticlassRace.Models;
using MulticlassRace.ViewModels;

namespace MulticlassRace.Mapping;

public static class RaceMappings
{
    public static RaceModel ToModel(this Race race)
    {
        return new RaceModel
        {
            RaceId = race.RaceId,
            RaceName = race.RaceName,
            Country = race.Country,
            Status = race.Status,
            PointSettingId = race.PointSettingId,
            PointSettingName = race.PointSetting.SettingName,
            ClassSettingId = race.ClassSettingId,
            ClassSettingName = race.ClassSetting?.ClassSettingName ?? string.Empty,
            TrackName = race.TrackName,
            TrackLayout = race.TrackLayout,
            NumberOfLaps = race.NumberOfLaps,
            RaceDuration = race.RaceDuration,
            PracticeSessionMinutes = race.PracticeSessionMinutes,
            QualifyingSessionMinutes = race.QualifyingSessionMinutes,
            StandingCount = race.Sessions.Sum(s => s.DriverStandings.Count)
        };
    }

    public static SessionModel ToModel(this Session session)
    {
        return new SessionModel
        {
            SessionId = session.SessionId,
            SessionType = session.SessionType,
            SessionDate = session.SessionDate,
            DriverStandings = session.DriverStandings
                .OrderBy(d => d.ClassPosition)
                .Select(d => d.ToModel())
                .ToList()
        };
    }

    public static DriverStandingModel ToModel(this DriverStanding standing)
    {
        return new DriverStandingModel
        {
            Position = standing.Position,
            DriverName = standing.DriverName,
            TeamName = standing.TeamName,
            TeamClassName = standing.TeamClassName,
            BestLapTimeMs = standing.BestLapTimeMs,
            Points = standing.Points,
            LapCount = standing.LapCount,
            ClassPosition = standing.ClassPosition
        };
    }
}
