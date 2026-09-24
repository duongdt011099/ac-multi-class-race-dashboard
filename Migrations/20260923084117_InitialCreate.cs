using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Championships",
                columns: table => new
                {
                    ChampionshipId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChampionshipName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Championships", x => x.ChampionshipId);
                });

            migrationBuilder.CreateTable(
                name: "TeamClasses",
                columns: table => new
                {
                    TeamClassId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeamClassName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamClasses", x => x.TeamClassId);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonName = table.Column<string>(type: "TEXT", nullable: false),
                    ChampionshipId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.SeasonId);
                    table.ForeignKey(
                        name: "FK_Seasons_Championships_ChampionshipId",
                        column: x => x.ChampionshipId,
                        principalTable: "Championships",
                        principalColumn: "ChampionshipId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeamName = table.Column<string>(type: "TEXT", nullable: false),
                    TeamLogo = table.Column<string>(type: "TEXT", nullable: true),
                    TeamClassId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.TeamId);
                    table.ForeignKey(
                        name: "FK_Teams_TeamClasses_TeamClassId",
                        column: x => x.TeamClassId,
                        principalTable: "TeamClasses",
                        principalColumn: "TeamClassId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Races",
                columns: table => new
                {
                    RaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RaceName = table.Column<string>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Races", x => x.RaceId);
                    table.ForeignKey(
                        name: "FK_Races_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "SeasonId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Driver",
                columns: table => new
                {
                    DriverId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DriverName = table.Column<string>(type: "TEXT", nullable: false),
                    Car = table.Column<string>(type: "TEXT", nullable: false),
                    Skin = table.Column<string>(type: "TEXT", nullable: false),
                    Nationality = table.Column<string>(type: "TEXT", nullable: true),
                    Age = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Driver", x => x.DriverId);
                    table.ForeignKey(
                        name: "FK_Driver_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId");
                });

            migrationBuilder.CreateTable(
                name: "DriverStandings",
                columns: table => new
                {
                    DriverStandingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DriverId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalTeamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeamName = table.Column<string>(type: "TEXT", nullable: false),
                    RaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Points = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverStandings", x => x.DriverStandingId);
                    table.ForeignKey(
                        name: "FK_DriverStandings_Driver_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Driver",
                        principalColumn: "DriverId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DriverStandings_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "RaceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Driver_TeamId",
                table: "Driver",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverStandings_DriverId",
                table: "DriverStandings",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverStandings_RaceId",
                table: "DriverStandings",
                column: "RaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Races_SeasonId",
                table: "Races",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_ChampionshipId",
                table: "Seasons",
                column: "ChampionshipId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_TeamClassId",
                table: "Teams",
                column: "TeamClassId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverStandings");

            migrationBuilder.DropTable(
                name: "Driver");

            migrationBuilder.DropTable(
                name: "Races");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropTable(
                name: "TeamClasses");

            migrationBuilder.DropTable(
                name: "Championships");
        }
    }
}
