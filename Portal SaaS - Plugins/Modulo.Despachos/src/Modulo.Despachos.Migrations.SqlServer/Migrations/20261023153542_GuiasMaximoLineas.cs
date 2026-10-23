using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class GuiasMaximoLineas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "guide_max_lines",
                table: "despachos_settings",
                type: "int",
                nullable: false,
                defaultValue: 60);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "guide_max_lines",
                table: "despachos_settings");
        }
    }
}
