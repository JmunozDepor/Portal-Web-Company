using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleExternalConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "module_external_connections",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    module_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    engine_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    database_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    technical_username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    technical_secret_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_external_connections", x => x.id);
                    table.CheckConstraint("ck_module_external_connections_engine_type", "engine_type in ('postgres', 'sqlserver')");
                    table.ForeignKey(
                        name: "fk_module_external_connections_company_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_module_external_connections_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_module_external_connections_company_id",
                table: "module_external_connections",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_module_external_connections_org_company_module",
                table: "module_external_connections",
                columns: new[] { "organization_id", "company_id", "module_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "module_external_connections");
        }
    }
}
