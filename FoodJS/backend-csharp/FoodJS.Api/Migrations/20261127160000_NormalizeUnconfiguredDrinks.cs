using FoodJS.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodJS.Api.Migrations;

// Data-only correction: never infer a free drink for an unconfigured item.
[DbContext(typeof(FoodJsDbContext))]
[Migration("20261127160000_NormalizeUnconfiguredDrinks")]
public sealed class NormalizeUnconfiguredDrinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        "UPDATE MenuItems SET DrinkOptionsJson = '[]' WHERE DrinkOptionsJson IS NULL;");

    // Restoring the obsolete implicit bundle would change item semantics. Keep the data.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
