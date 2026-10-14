using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.AuditoriaInventario.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPwaUrlToCaptureSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pwa_url",
                table: "capture_settings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pwa_url",
                table: "capture_settings");
        }
    }
}
