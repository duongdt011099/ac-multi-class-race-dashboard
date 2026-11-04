using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonClassSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClassSettingId",
                table: "Seasons",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_ClassSettingId",
                table: "Seasons",
                column: "ClassSettingId");

            migrationBuilder.AddForeignKey(
                name: "FK_Seasons_ClassSettings_ClassSettingId",
                table: "Seasons",
                column: "ClassSettingId",
                principalTable: "ClassSettings",
                principalColumn: "ClassSettingId",
                onDelete: ReferentialAction.SetNull);

            // Existing seasons adopt the class setting their races share. A season that mixes several
            // (or has no races yet) stays unset, and its races keep the setting they were made with.
            migrationBuilder.Sql(
                """
                UPDATE Seasons
                SET ClassSettingId = (SELECT MIN(r.ClassSettingId) FROM Races r WHERE r.SeasonId = Seasons.SeasonId)
                WHERE (SELECT COUNT(DISTINCT r.ClassSettingId) FROM Races r WHERE r.SeasonId = Seasons.SeasonId) = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Seasons_ClassSettings_ClassSettingId",
                table: "Seasons");

            migrationBuilder.DropIndex(
                name: "IX_Seasons_ClassSettingId",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "ClassSettingId",
                table: "Seasons");
        }
    }
}
