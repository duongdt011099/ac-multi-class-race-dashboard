using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddRaceTrackInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NumberOfLaps",
                table: "Races",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PracticeSessionMinutes",
                table: "Races",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualifyingSessionMinutes",
                table: "Races",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RaceDuration",
                table: "Races",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackLayout",
                table: "Races",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackName",
                table: "Races",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumberOfLaps",
                table: "Races");

            migrationBuilder.DropColumn(
                name: "PracticeSessionMinutes",
                table: "Races");

            migrationBuilder.DropColumn(
                name: "QualifyingSessionMinutes",
                table: "Races");

            migrationBuilder.DropColumn(
                name: "RaceDuration",
                table: "Races");

            migrationBuilder.DropColumn(
                name: "TrackLayout",
                table: "Races");

            migrationBuilder.DropColumn(
                name: "TrackName",
                table: "Races");
        }
    }
}
