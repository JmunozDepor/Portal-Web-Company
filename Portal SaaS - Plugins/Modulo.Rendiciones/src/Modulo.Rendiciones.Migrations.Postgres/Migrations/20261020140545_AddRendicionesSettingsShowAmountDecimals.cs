using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Rendiciones.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddRendicionesSettingsShowAmountDecimals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "show_amount_decimals",
                table: "rendiciones_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "show_amount_decimals",
                table: "rendiciones_settings");
        }
    }
}
