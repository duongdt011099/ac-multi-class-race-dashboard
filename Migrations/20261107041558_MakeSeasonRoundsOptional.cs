using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class MakeSeasonRoundsOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "NumberOfRounds",
                table: "Seasons",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            // Seasons that existed before rounds were configurable have no real limit: the user has to set it.
            migrationBuilder.Sql("UPDATE Seasons SET NumberOfRounds = NULL WHERE IsFinished = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Seasons SET NumberOfRounds = 8 WHERE NumberOfRounds IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "NumberOfRounds",
                table: "Seasons",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
