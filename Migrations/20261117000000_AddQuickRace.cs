using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MulticlassRace.Data;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261117000000_AddQuickRace")]
    public partial class AddQuickRace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuickRaceResults",
                columns: table => new
                {
                    QuickRaceResultId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RaceDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    TrackName = table.Column<string>(type: "TEXT", nullable: true),
                    TrackLayout = table.Column<string>(type: "TEXT", nullable: true),
                    DriverCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HumanPosition = table.Column<int>(type: "INTEGER", nullable: true),
                    HumanClassPosition = table.Column<int>(type: "INTEGER", nullable: true),
                    HumanClassName = table.Column<string>(type: "TEXT", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickRaceResults", x => x.QuickRaceResultId);
                });

            migrationBuilder.CreateTable(
                name: "QuickRaceSetups",
                columns: table => new
                {
                    QuickRaceSetupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SetupJson = table.Column<string>(type: "TEXT", nullable: false),
                    LastLaunchJson = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickRaceSetups", x => x.QuickRaceSetupId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "QuickRaceResults");

            migrationBuilder.DropTable(name: "QuickRaceSetups");
        }
    }
}
