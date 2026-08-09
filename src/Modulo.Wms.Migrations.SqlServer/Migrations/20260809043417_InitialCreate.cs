using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Wms.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wms_oracle_field_mappings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mapper_key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    field_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    value_template = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_oracle_field_mappings", x => x.id);
                });

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

            migrationBuilder.CreateTable(
                name: "wms_oracle_service_heartbeats",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    processor_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    config_source_effective = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_oracle_service_heartbeats", x => new { x.company_id, x.processor_key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_wms_oracle_field_mappings_company_id",
                table: "wms_oracle_field_mappings",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uk_wms_oracle_field_mappings_key",
                table: "wms_oracle_field_mappings",
                columns: new[] { "company_id", "mapper_key", "field_name" },
                unique: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_oracle_field_mappings");

            migrationBuilder.DropTable(
                name: "wms_oracle_service_configs");

            migrationBuilder.DropTable(
                name: "wms_oracle_service_heartbeats");
        }
    }
}
