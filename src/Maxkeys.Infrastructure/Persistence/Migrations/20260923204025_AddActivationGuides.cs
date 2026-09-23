using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maxkeys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivationGuides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "activation_guide",
                table: "products");

            migrationBuilder.AddColumn<Guid>(
                name: "activation_guide_id",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "activation_guides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_markdown = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activation_guides", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_activation_guide_id",
                table: "products",
                column: "activation_guide_id");

            migrationBuilder.CreateIndex(
                name: "ix_activation_guides_slug",
                table: "activation_guides",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activation_guides");

            migrationBuilder.DropIndex(
                name: "ix_products_activation_guide_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "activation_guide_id",
                table: "products");

            migrationBuilder.AddColumn<string>(
                name: "activation_guide",
                table: "products",
                type: "text",
                nullable: true);
        }
    }
}
