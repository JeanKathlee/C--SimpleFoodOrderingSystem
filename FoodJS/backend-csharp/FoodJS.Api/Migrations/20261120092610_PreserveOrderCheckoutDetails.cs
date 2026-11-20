using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodJS.Api.Migrations
{
    /// <inheritdoc />
    public partial class PreserveOrderCheckoutDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CouponCode",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MobileNumber",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            // Original checkout contacts were not stored. Freeze the currently available
            // profile values for legacy orders; their applied coupon code cannot be recovered.
            migrationBuilder.Sql("""
                UPDATE "Orders"
                SET "CustomerName" = COALESCE(
                    (SELECT trim("Users"."FirstName" || ' ' || "Users"."LastName")
                     FROM "Users" WHERE "Users"."Id" = "Orders"."UserId"), ''),
                    "MobileNumber" = COALESCE(
                    (SELECT "Users"."MobileNumber"
                     FROM "Users" WHERE "Users"."Id" = "Orders"."UserId"), '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CouponCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MobileNumber",
                table: "Orders");
        }
    }
}
