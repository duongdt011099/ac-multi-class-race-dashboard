using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddClassSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite cannot drop a table that is still referenced, and it ignores a foreign_keys
            // toggle made inside the migration's transaction. Turning enforcement off outside the
            // transaction lets the Teams table be rebuilt and the old TeamClasses table be dropped.
            migrationBuilder.Sql("PRAGMA foreign_keys = OFF;", suppressTransaction: true);

            migrationBuilder.DropForeignKey(
                name: "FK_Teams_TeamClasses_TeamClassId",
                table: "Teams");

            migrationBuilder.DropTable(
                name: "TeamClasses");

            migrationBuilder.RenameColumn(
                name: "TeamClassId",
                table: "Teams",
                newName: "ClassSettingItemId");

            migrationBuilder.RenameIndex(
                name: "IX_Teams_TeamClassId",
                table: "Teams",
                newName: "IX_Teams_ClassSettingItemId");

            migrationBuilder.AddColumn<Guid>(
                name: "ClassSettingId",
                table: "Races",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("2254e881-f300-4154-a84f-600196de6000"));

            migrationBuilder.CreateTable(
                name: "ClassSettings",
                columns: table => new
                {
                    ClassSettingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClassSettingName = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassSettings", x => x.ClassSettingId);
                });

            migrationBuilder.CreateTable(
                name: "ClassSettingItems",
                columns: table => new
                {
                    ClassSettingItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClassSettingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassSettingItems", x => x.ClassSettingItemId);
                    table.ForeignKey(
                        name: "FK_ClassSettingItems_ClassSettings_ClassSettingId",
                        column: x => x.ClassSettingId,
                        principalTable: "ClassSettings",
                        principalColumn: "ClassSettingId",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.InsertData(
                table: "ClassSettings",
                columns: new[] { "ClassSettingId", "ClassSettingName", "IsDefault" },
                values: new object[] { new Guid("2254e881-f300-4154-a84f-600196de6000"), "WEC", true });

            migrationBuilder.InsertData(
                table: "ClassSettingItems",
                columns: new[] { "ClassSettingItemId", "ClassSettingId", "Color", "Name", "Order" },
                values: new object[,]
                {
                    { new Guid("2254e881-f300-4154-a84f-600196de6081"), new Guid("2254e881-f300-4154-a84f-600196de6000"), "#e10600", "Hypercar", 1 },
                    { new Guid("2254e881-f300-4154-a84f-600196de6082"), new Guid("2254e881-f300-4154-a84f-600196de6000"), "#0057b8", "LMP2", 2 },
                    { new Guid("2254e881-f300-4154-a84f-600196de6083"), new Guid("2254e881-f300-4154-a84f-600196de6000"), "#3fa34d", "GT3", 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Races_ClassSettingId",
                table: "Races",
                column: "ClassSettingId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSettingItems_ClassSettingId",
                table: "ClassSettingItems",
                column: "ClassSettingId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverClassSettingItem_DriversDriverId",
                table: "DriverClassSettingItem",
                column: "DriversDriverId");

            migrationBuilder.AddForeignKey(
                name: "FK_Races_ClassSettings_ClassSettingId",
                table: "Races",
                column: "ClassSettingId",
                principalTable: "ClassSettings",
                principalColumn: "ClassSettingId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_ClassSettingItems_ClassSettingItemId",
                table: "Teams",
                column: "ClassSettingItemId",
                principalTable: "ClassSettingItems",
                principalColumn: "ClassSettingItemId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("PRAGMA foreign_keys = ON;", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Races_ClassSettings_ClassSettingId",
                table: "Races");

            migrationBuilder.DropForeignKey(
                name: "FK_Teams_ClassSettingItems_ClassSettingItemId",
                table: "Teams");

            migrationBuilder.DropTable(
                name: "DriverClassSettingItem");

            migrationBuilder.DropTable(
                name: "ClassSettingItems");

            migrationBuilder.DropTable(
                name: "ClassSettings");

            migrationBuilder.DropIndex(
                name: "IX_Races_ClassSettingId",
                table: "Races");

            migrationBuilder.DropColumn(
                name: "ClassSettingId",
                table: "Races");

            migrationBuilder.RenameColumn(
                name: "ClassSettingItemId",
                table: "Teams",
                newName: "TeamClassId");

            migrationBuilder.RenameIndex(
                name: "IX_Teams_ClassSettingItemId",
                table: "Teams",
                newName: "IX_Teams_TeamClassId");

            migrationBuilder.CreateTable(
                name: "TeamClasses",
                columns: table => new
                {
                    TeamClassId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    TeamClassName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamClasses", x => x.TeamClassId);
                });

            migrationBuilder.InsertData(
                table: "TeamClasses",
                columns: new[] { "TeamClassId", "IsActive", "TeamClassName" },
                values: new object[,]
                {
                    { new Guid("2254e881-f300-4154-a84f-600196de6081"), true, "Hypercar" },
                    { new Guid("2254e881-f300-4154-a84f-600196de6082"), true, "LMP2" },
                    { new Guid("2254e881-f300-4154-a84f-600196de6083"), true, "GT3" }
                });

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_TeamClasses_TeamClassId",
                table: "Teams",
                column: "TeamClassId",
                principalTable: "TeamClasses",
                principalColumn: "TeamClassId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
