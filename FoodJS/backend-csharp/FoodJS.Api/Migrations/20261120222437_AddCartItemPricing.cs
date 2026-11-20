using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodJS.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCartItemPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "CartItems",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                "UPDATE CartItems SET UnitPrice = " +
                "COALESCE((SELECT Price FROM MenuItems WHERE MenuItems.Id = CartItems.MenuItemId), 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "CartItems");
        }
    }
}
