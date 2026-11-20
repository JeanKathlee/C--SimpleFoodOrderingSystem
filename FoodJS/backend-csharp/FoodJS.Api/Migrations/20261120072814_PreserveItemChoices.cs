using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodJS.Api.Migrations
{
    /// <inheritdoc />
    public partial class PreserveItemChoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SelectedChoice",
                table: "OrderItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedChoice",
                table: "CartItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SelectedChoice",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "SelectedChoice",
                table: "CartItems");
        }
    }
}
