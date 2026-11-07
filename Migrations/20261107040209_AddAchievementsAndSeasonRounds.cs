using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddAchievementsAndSeasonRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FinishedAt",
                table: "Seasons",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFinished",
                table: "Seasons",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NumberOfRounds",
                table: "Seasons",
                type: "INTEGER",
                nullable: false,
                defaultValue: 8);

            // Seasons that already hold more than the default number of races keep them all as rounds.
            migrationBuilder.Sql(
                """
                UPDATE Seasons
                SET NumberOfRounds = (SELECT COUNT(*) FROM Races r WHERE r.SeasonId = Seasons.SeasonId)
                WHERE (SELECT COUNT(*) FROM Races r WHERE r.SeasonId = Seasons.SeasonId) > 8;
                """);

            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    AchievementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    DriverId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DriverName = table.Column<string>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChampionshipName = table.Column<string>(type: "TEXT", nullable: false),
                    SeasonName = table.Column<string>(type: "TEXT", nullable: false),
                    RaceName = table.Column<string>(type: "TEXT", nullable: true),
                    TrackName = table.Column<string>(type: "TEXT", nullable: true),
                    ClassName = table.Column<string>(type: "TEXT", nullable: true),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    EarnedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.AchievementId);
                    table.ForeignKey(
                        name: "FK_Achievements_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "RaceId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Achievements_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "SeasonId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_DriverId",
                table: "Achievements",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_RaceId",
                table: "Achievements",
                column: "RaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_SeasonId",
                table: "Achievements",
                column: "SeasonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Achievements");

            migrationBuilder.DropColumn(
                name: "FinishedAt",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "IsFinished",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "NumberOfRounds",
                table: "Seasons");
        }
    }
}
