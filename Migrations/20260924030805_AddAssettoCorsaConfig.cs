using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddAssettoCorsaConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssettoCorsaGameConfigs",
                columns: table => new
                {
                    AssettoCorsaGameConfigId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GamePath = table.Column<string>(type: "TEXT", nullable: false),
                    PresetPath = table.Column<string>(type: "TEXT", nullable: false),
                    RaceResultsPath = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssettoCorsaGameConfigs", x => x.AssettoCorsaGameConfigId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssettoCorsaGameConfigs");
        }
    }
}
