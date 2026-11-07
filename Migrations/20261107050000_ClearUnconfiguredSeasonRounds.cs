using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MulticlassRace.Data;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261107050000_ClearUnconfiguredSeasonRounds")]
    public partial class ClearUnconfiguredSeasonRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Seasons that existed before rounds were configurable have no real limit: the user has to set it.
            migrationBuilder.Sql("UPDATE Seasons SET NumberOfRounds = NULL WHERE IsFinished = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Seasons SET NumberOfRounds = 8 WHERE NumberOfRounds IS NULL;");
        }
    }
}
