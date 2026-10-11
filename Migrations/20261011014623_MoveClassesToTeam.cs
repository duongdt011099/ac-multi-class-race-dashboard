using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class MoveClassesToTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the team -> class join table first, then copy each team's
            // existing single class into it before the old column is dropped.
            migrationBuilder.CreateTable(
                name: "TeamClassSettingItem",
                columns: table => new
                {
                    ClassSettingItemsClassSettingItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeamsTeamId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamClassSettingItem", x => new { x.ClassSettingItemsClassSettingItemId, x.TeamsTeamId });
                    table.ForeignKey(
                        name: "FK_TeamClassSettingItem_ClassSettingItems_ClassSettingItemsClassSettingItemId",
                        column: x => x.ClassSettingItemsClassSettingItemId,
                        principalTable: "ClassSettingItems",
                        principalColumn: "ClassSettingItemId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamClassSettingItem_Teams_TeamsTeamId",
                        column: x => x.TeamsTeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                "INSERT INTO TeamClassSettingItem (TeamsTeamId, ClassSettingItemsClassSettingItemId) " +
                "SELECT TeamId, ClassSettingItemId FROM Teams;");

            migrationBuilder.CreateIndex(
                name: "IX_TeamClassSettingItem_TeamsTeamId",
                table: "TeamClassSettingItem",
                column: "TeamsTeamId");

            migrationBuilder.DropForeignKey(
                name: "FK_Teams_ClassSettingItems_ClassSettingItemId",
                table: "Teams");

            migrationBuilder.DropTable(
                name: "DriverClassSettingItem");

            migrationBuilder.DropIndex(
                name: "IX_Teams_ClassSettingItemId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "ClassSettingItemId",
                table: "Teams");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClassSettingItemId",
                table: "Teams",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Best-effort restore of each team's single class from the join table.
            migrationBuilder.Sql(
                "UPDATE Teams SET ClassSettingItemId = (" +
                "SELECT t.ClassSettingItemsClassSettingItemId FROM TeamClassSettingItem t " +
                "WHERE t.TeamsTeamId = Teams.TeamId LIMIT 1) " +
                "WHERE EXISTS (SELECT 1 FROM TeamClassSettingItem t WHERE t.TeamsTeamId = Teams.TeamId);");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ClassSettingItemId",
                table: "Teams",
                column: "ClassSettingItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_ClassSettingItems_ClassSettingItemId",
                table: "Teams",
                column: "ClassSettingItemId",
                principalTable: "ClassSettingItems",
                principalColumn: "ClassSettingItemId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateTable(
                name: "DriverClassSettingItem",
                columns: table => new
                {
                    ClassSettingItemsClassSettingItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DriversDriverId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverClassSettingItem", x => new { x.ClassSettingItemsClassSettingItemId, x.DriversDriverId });
                    table.ForeignKey(
                        name: "FK_DriverClassSettingItem_ClassSettingItems_ClassSettingItemsClassSettingItemId",
                        column: x => x.ClassSettingItemsClassSettingItemId,
                        principalTable: "ClassSettingItems",
                        principalColumn: "ClassSettingItemId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DriverClassSettingItem_Driver_DriversDriverId",
                        column: x => x.DriversDriverId,
                        principalTable: "Driver",
                        principalColumn: "DriverId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DriverClassSettingItem_DriversDriverId",
                table: "DriverClassSettingItem",
                column: "DriversDriverId");

            migrationBuilder.DropTable(
                name: "TeamClassSettingItem");
        }
    }
}
