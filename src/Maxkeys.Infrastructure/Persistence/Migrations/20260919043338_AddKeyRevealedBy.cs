using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maxkeys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKeyRevealedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "revealed_by",
                table: "keys",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "revealed_by",
                table: "keys");
        }
    }
}
