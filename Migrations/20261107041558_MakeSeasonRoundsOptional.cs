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
            // The data fix lives in the next migration: SQLite rebuilds the table after all operations
            // in this one, so an UPDATE here would run while the column is still NOT NULL.
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
