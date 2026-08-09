using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modulo.Wms.Migrations.Postgres.Migrations
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
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mapper_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value_template = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
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
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    config_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_oracle_service_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_oracle_service_heartbeats",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processor_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    config_source_effective = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
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
