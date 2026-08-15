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
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    modulo_origen = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entidad_negocio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    conector_tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    conector_config_cifrado = table.Column<string>(type: "text", nullable: false),
                    direccion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    programacion_cron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_definitions", x => x.id);
                    table.CheckConstraint("ck_integration_definitions_conector_tipo", "conector_tipo in ('Sap', 'Rest', 'Archivo')");
                    table.CheckConstraint("ck_integration_definitions_direccion", "direccion in ('Subida', 'Bajada', 'Ambas')");
                });

            migrationBuilder.CreateTable(
                name: "integration_run_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    integration_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    iniciado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finalizado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    registros_procesados = table.Column<int>(type: "integer", nullable: false),
                    registros_con_error = table.Column<int>(type: "integer", nullable: false),
                    detalle_error = table.Column<string>(type: "text", nullable: true),
                    disparado_por = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_run_logs", x => x.id);
                    table.CheckConstraint("ck_integration_run_logs_disparado_por", "disparado_por in ('Programado', 'Manual')");
                    table.CheckConstraint("ck_integration_run_logs_resultado", "resultado in ('Exito', 'Error', 'Parcial')");
                });

            migrationBuilder.CreateTable(
                name: "integration_field_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campo_local = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    campo_externo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    transformacion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
