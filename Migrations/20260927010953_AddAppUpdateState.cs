using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddAppUpdateState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUpdateStates",
                columns: table => new
                {
                    AppUpdateStateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastSeenVersion = table.Column<string>(type: "TEXT", nullable: false),
                    LastCheckedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUpdateStates", x => x.AppUpdateStateId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUpdateStates");
        }
    }
}
