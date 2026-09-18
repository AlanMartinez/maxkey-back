using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maxkeys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVaultSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "vault_enabled",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "keys",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "vault_enabled",
                table: "products");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "keys");
        }
    }
}
