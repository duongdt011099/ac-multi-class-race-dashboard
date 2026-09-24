using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BestLapTimeMs",
                table: "DriverStandings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "DriverStandings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Session",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionType = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RaceId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Session", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_Session_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "RaceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DriverStandings_SessionId",
                table: "DriverStandings",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Session_RaceId",
                table: "Session",
                column: "RaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_DriverStandings_Session_SessionId",
                table: "DriverStandings",
                column: "SessionId",
                principalTable: "Session",
                principalColumn: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DriverStandings_Session_SessionId",
                table: "DriverStandings");

            migrationBuilder.DropTable(
                name: "Session");

            migrationBuilder.DropIndex(
                name: "IX_DriverStandings_SessionId",
                table: "DriverStandings");

            migrationBuilder.DropColumn(
                name: "BestLapTimeMs",
                table: "DriverStandings");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "DriverStandings");
        }
    }
}
