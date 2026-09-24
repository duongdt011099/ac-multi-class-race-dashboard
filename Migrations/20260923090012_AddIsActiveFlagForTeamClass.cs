using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActiveFlagForTeamClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "TeamClasses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "TeamClasses");
        }
    }
}
