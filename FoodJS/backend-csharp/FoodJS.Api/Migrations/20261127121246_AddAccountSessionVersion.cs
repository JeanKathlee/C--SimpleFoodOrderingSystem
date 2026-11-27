using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodJS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthVersion",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthVersion",
                table: "Users");
        }
    }
}
