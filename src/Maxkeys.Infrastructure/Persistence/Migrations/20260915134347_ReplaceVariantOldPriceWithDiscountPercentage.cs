using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maxkeys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceVariantOldPriceWithDiscountPercentage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "discount_percentage",
                table: "product_variants",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.Sql(@"UPDATE product_variants SET discount_percentage = ROUND((1 - (price / old_price)) * 100, 2)
      WHERE old_price IS NOT NULL AND old_price > price;");

            migrationBuilder.Sql(@"UPDATE product_variants SET discount_percentage = NULL
      WHERE discount_percentage IS NOT NULL AND (discount_percentage <= 0 OR discount_percentage >= 100);");

            migrationBuilder.DropColumn(
                name: "old_price",
                table: "product_variants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "old_price",
                table: "product_variants",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.Sql(@"UPDATE product_variants SET old_price = ROUND(price / (1 - discount_percentage / 100), 2)
      WHERE discount_percentage IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "discount_percentage",
                table: "product_variants");
        }
    }
}
