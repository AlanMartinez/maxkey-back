using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maxkeys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantIsRecommended : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_recommended",
                table: "product_variants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_product_id_is_recommended",
                table: "product_variants",
                column: "product_id",
                unique: true,
                filter: "is_recommended = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_product_variants_product_id_is_recommended",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "is_recommended",
                table: "product_variants");
        }
    }
}
