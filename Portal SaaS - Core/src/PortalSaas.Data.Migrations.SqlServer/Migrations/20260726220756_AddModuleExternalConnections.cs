using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
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
                        .Annotation("SqlServer:Identity", "1, 1"),
                    organization_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    module_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    engine_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    host = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    port = table.Column<int>(type: "int", nullable: false),
                    database_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    technical_username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    technical_secret_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                unique: true,
                filter: "[company_id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "module_external_connections");
        }
    }
}
