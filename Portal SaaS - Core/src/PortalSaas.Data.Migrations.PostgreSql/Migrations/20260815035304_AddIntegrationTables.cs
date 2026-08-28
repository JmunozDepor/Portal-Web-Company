using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_module = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    business_entity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    connector_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    encrypted_connector_config = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    cron_schedule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_definitions", x => x.id);
                    table.CheckConstraint("ck_integration_definitions_connector_type", "connector_type in ('sap', 'rest', 'file')");
                    table.CheckConstraint("ck_integration_definitions_direction", "direction in ('upload', 'download', 'both')");
                });

            migrationBuilder.CreateTable(
                name: "integration_run_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    integration_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    records_processed = table.Column<int>(type: "integer", nullable: false),
                    records_failed = table.Column<int>(type: "integer", nullable: false),
                    error_detail = table.Column<string>(type: "text", nullable: true),
                    triggered_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_run_logs", x => x.id);
                    table.CheckConstraint("ck_integration_run_logs_status", "status in ('success', 'error', 'partial')");
                    table.CheckConstraint("ck_integration_run_logs_triggered_by", "triggered_by in ('scheduled', 'manual')");
                });

            migrationBuilder.CreateTable(
                name: "integration_field_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    transformation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_field_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_integration_field_mappings_integration_definitions_integrat",
                        column: x => x.integration_definition_id,
                        principalTable: "integration_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_integration_definitions_company_id_is_active",
                table: "integration_definitions",
                columns: new[] { "company_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_field_mappings_integration_definition_id",
                table: "integration_field_mappings",
                column: "integration_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_run_logs_integration_definition_id",
                table: "integration_run_logs",
                column: "integration_definition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_field_mappings");

            migrationBuilder.DropTable(
                name: "integration_run_logs");

            migrationBuilder.DropTable(
                name: "integration_definitions");
        }
    }
}
