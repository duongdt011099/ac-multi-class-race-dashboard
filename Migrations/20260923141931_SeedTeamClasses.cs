using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class SeedTeamClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "TeamClasses",
                columns: new[] { "TeamClassId", "IsActive", "TeamClassName" },
                values: new object[,]
                {
                    { new Guid("2254e881-f300-4154-a84f-600196de6081"), true, "Hypercar" },
                    { new Guid("2254e881-f300-4154-a84f-600196de6082"), true, "LMP2" },
                    { new Guid("2254e881-f300-4154-a84f-600196de6083"), true, "GT3" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "TeamClasses",
                keyColumn: "TeamClassId",
                keyValue: new Guid("2254e881-f300-4154-a84f-600196de6081"));

            migrationBuilder.DeleteData(
                table: "TeamClasses",
                keyColumn: "TeamClassId",
                keyValue: new Guid("2254e881-f300-4154-a84f-600196de6082"));

            migrationBuilder.DeleteData(
                table: "TeamClasses",
                keyColumn: "TeamClassId",
                keyValue: new Guid("2254e881-f300-4154-a84f-600196de6083"));
        }
    }
}
