using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodJS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuDrinkOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DrinkOptionsJson",
                table: "MenuItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DrinkOptionsJson",
                table: "MenuItems");
        }
    }
}
