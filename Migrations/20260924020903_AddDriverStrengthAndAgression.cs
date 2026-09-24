using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace multi_class_race_dashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverStrengthAndAgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DriverAgression",
                table: "Driver",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DriverStrength",
                table: "Driver",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriverAgression",
                table: "Driver");

            migrationBuilder.DropColumn(
                name: "DriverStrength",
                table: "Driver");
        }
    }
}
