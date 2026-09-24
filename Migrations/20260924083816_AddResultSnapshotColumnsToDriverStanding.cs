using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddResultSnapshotColumnsToDriverStanding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClassPosition",
                table: "DriverStandings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DriverName",
                table: "DriverStandings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LapCount",
                table: "DriverStandings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TeamClassName",
                table: "DriverStandings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClassPosition",
                table: "DriverStandings");

            migrationBuilder.DropColumn(
                name: "DriverName",
                table: "DriverStandings");

            migrationBuilder.DropColumn(
                name: "LapCount",
                table: "DriverStandings");

            migrationBuilder.DropColumn(
                name: "TeamClassName",
                table: "DriverStandings");
        }
    }
}
