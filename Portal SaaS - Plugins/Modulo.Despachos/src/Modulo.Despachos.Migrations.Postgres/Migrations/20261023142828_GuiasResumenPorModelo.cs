using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class GuiasResumenPorModelo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "guide_line_grouping",
                table: "despachos_settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "sku");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "guide_line_grouping",
                table: "despachos_settings");
        }
    }
}
