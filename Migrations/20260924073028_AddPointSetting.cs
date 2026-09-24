using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddPointSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PointSettingId",
                table: "Races",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "PointSettings",
                columns: table => new
                {
                    SettingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    SettingName = table.Column<string>(type: "TEXT", nullable: false),
                    Config = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointSettings", x => x.SettingId);
                });

            migrationBuilder.InsertData(
                table: "PointSettings",
                columns: new[] { "SettingId", "IsDefault", "SettingName", "Config" },
                values: new object[,]
                {
                    { new Guid("d8f10b2a-5a6b-4c7d-8e9f-0a1b2c3d4e5f"), true, "Standard", "{\"1\":25,\"2\":18,\"3\":15,\"4\":12,\"5\":10,\"6\":8,\"7\":6,\"8\":4,\"9\":2,\"10\":1}" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Races_PointSettingId",
                table: "Races",
                column: "PointSettingId");

            migrationBuilder.AddForeignKey(
                name: "FK_Races_PointSettings_PointSettingId",
                table: "Races",
                column: "PointSettingId",
                principalTable: "PointSettings",
                principalColumn: "SettingId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Races_PointSettings_PointSettingId",
                table: "Races");

            migrationBuilder.DropTable(
                name: "PointSettings");

            migrationBuilder.DropIndex(
                name: "IX_Races_PointSettingId",
                table: "Races");

            migrationBuilder.DropColumn(
                name: "PointSettingId",
                table: "Races");
        }
    }
}
