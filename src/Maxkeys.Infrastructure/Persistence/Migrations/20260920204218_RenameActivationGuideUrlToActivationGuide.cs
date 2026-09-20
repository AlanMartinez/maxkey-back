using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maxkeys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameActivationGuideUrlToActivationGuide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "activation_guide_url",
                table: "products",
                newName: "activation_guide");

            migrationBuilder.AlterColumn<string>(
                name: "activation_guide",
                table: "products",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "activation_guide",
                table: "products",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "activation_guide",
                table: "products",
                newName: "activation_guide_url");
        }
    }
}
