using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.AuditoriaInventario.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capture_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    allow_ean8 = table.Column<bool>(type: "bit", nullable: false),
                    allow_upca = table.Column<bool>(type: "bit", nullable: false),
                    allow_ean13 = table.Column<bool>(type: "bit", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capture_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_capture_settings_company_id",
                table: "capture_settings",
                column: "company_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capture_settings");
        }
    }
}
