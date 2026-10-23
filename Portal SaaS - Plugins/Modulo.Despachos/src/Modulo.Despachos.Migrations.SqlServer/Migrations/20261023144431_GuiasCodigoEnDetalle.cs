using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class GuiasCodigoEnDetalle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "guide_item_code_placement",
                table: "despachos_settings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "column");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "guide_item_code_placement",
                table: "despachos_settings");
        }
    }
}
