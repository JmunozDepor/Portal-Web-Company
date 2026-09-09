using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Wms.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeSettingsReemplazaServiceConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_oracle_service_configs");

            migrationBuilder.CreateTable(
                name: "wms_runtime_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    clave = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    valor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_runtime_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uk_wms_runtime_settings_clave",
                table: "wms_runtime_settings",
                column: "clave",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_runtime_settings");

            migrationBuilder.CreateTable(
                name: "wms_oracle_service_configs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    config_key = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    config_value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_oracle_service_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wms_oracle_service_configs_company_id",
                table: "wms_oracle_service_configs",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uk_wms_oracle_service_configs_key",
                table: "wms_oracle_service_configs",
                columns: new[] { "company_id", "config_key" },
                unique: true);
        }
    }
}
