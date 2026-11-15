using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MulticlassRace.Data;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261115090000_BackfillRacePodiumAchievements")]
    public partial class BackfillRacePodiumAchievements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Race podiums (2nd/3rd in class) were not recorded before. Mirrors SeasonCompletionService.SyncRaceWinAsync.
            // Type 4 = RaceSecond, 5 = RaceThird.
            migrationBuilder.Sql(
                """
                INSERT INTO Achievements
                    (AchievementId, Type, DriverId, DriverName, SeasonId, RaceId, ChampionshipName, SeasonName,
                     RaceName, TrackName, ClassName, Points, Position, EarnedAt)
                SELECT
                    hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) ||
                        '-A' || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6)),
                    CASE ds.ClassPosition WHEN 2 THEN 4 ELSE 5 END,
                    ds.DriverId, ds.DriverName, s.SeasonId, r.RaceId, c.ChampionshipName, s.SeasonName,
                    r.RaceName, r.TrackName, ds.TeamClassName, ds.Points, ds.ClassPosition, se.SessionDate
                FROM Races r
                JOIN Seasons s ON s.SeasonId = r.SeasonId
                JOIN Championships c ON c.ChampionshipId = s.ChampionshipId
                JOIN Session se ON se.RaceId = r.RaceId AND se.SessionType = 2
                    AND se.SessionDate = (SELECT MAX(x.SessionDate) FROM Session x
                                          WHERE x.RaceId = r.RaceId AND x.SessionType = 2)
                JOIN DriverStandings ds ON ds.SessionId = se.SessionId AND ds.ClassPosition IN (2, 3)
                JOIN Driver d ON d.DriverId = ds.DriverId AND d.IsHuman = 1
                WHERE r.Status = 2
                  AND NOT EXISTS (SELECT 1 FROM Achievements a WHERE a.RaceId = r.RaceId);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
