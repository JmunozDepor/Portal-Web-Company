using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyExternalConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_external_connections",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    base_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    port = table.Column<int>(type: "integer", nullable: true),
                    database_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    technical_username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    technical_secret_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    configuracion_extra = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_external_connections", x => x.id);
                    table.CheckConstraint("ck_company_external_connections_tipo", "tipo in ('db_postgres', 'db_sqlserver', 'db_hana', 'http_api')");
                    table.ForeignKey(
                        name: "fk_company_external_connections_company_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_module_connection",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    connection_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_module_connection", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_module_connection_company_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_company_module_connection_company_external_connection_conne",
                        column: x => x.connection_id,
                        principalTable: "company_external_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_company_external_connections_company_nombre",
                table: "company_external_connections",
                columns: new[] { "company_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_module_connection_connection_id",
                table: "company_module_connection",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "uq_company_module_connection_company_module_purpose",
                table: "company_module_connection",
                columns: new[] { "company_id", "module_code", "purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_module_connection");

            migrationBuilder.DropTable(
                name: "company_external_connections");
        }
    }
}
