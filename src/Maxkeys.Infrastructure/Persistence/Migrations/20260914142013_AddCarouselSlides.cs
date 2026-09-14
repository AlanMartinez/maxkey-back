using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maxkeys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCarouselSlides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carousel_slides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    image_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carousel_slides", x => x.id);
                    table.ForeignKey(
                        name: "fk_carousel_slides_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_carousel_slides_is_active_sort_order",
                table: "carousel_slides",
                columns: new[] { "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_carousel_slides_product_id",
                table: "carousel_slides",
                column: "product_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "carousel_slides");
        }
    }
}
