using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddLuaResultImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LuaResultPath",
                table: "AssettoCorsaGameConfigs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ImportedLuaResults",
                columns: table => new
                {
                    ImportedLuaResultId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FullPath = table.Column<string>(type: "TEXT", nullable: false),
                    SessionType = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Imported = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ImportCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedLuaResults", x => x.ImportedLuaResultId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedLuaResults_FileName_SessionType",
                table: "ImportedLuaResults",
                columns: new[] { "FileName", "SessionType" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedLuaResults_FullPath",
                table: "ImportedLuaResults",
                column: "FullPath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportedLuaResults");

            migrationBuilder.DropColumn(
                name: "LuaResultPath",
                table: "AssettoCorsaGameConfigs");
        }
    }
}
