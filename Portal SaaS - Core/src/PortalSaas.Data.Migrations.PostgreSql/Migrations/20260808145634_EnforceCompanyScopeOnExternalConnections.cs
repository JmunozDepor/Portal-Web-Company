using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class EnforceCompanyScopeOnExternalConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_module_external_connections_company_company_id",
                table: "module_external_connections");

            migrationBuilder.DropForeignKey(
                name: "fk_module_external_connections_organizations_organization_id",
                table: "module_external_connections");

            migrationBuilder.DropIndex(
                name: "ix_module_external_connections_company_id",
                table: "module_external_connections");

            migrationBuilder.DropIndex(
                name: "uq_module_external_connections_org_company_module",
                table: "module_external_connections");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "module_external_connections");

            migrationBuilder.AlterColumn<Guid>(
                name: "company_id",
                table: "module_external_connections",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_module_external_connections_company_module",
                table: "module_external_connections",
                columns: new[] { "company_id", "module_code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_module_external_connections_company_company_id",
                table: "module_external_connections",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_module_external_connections_company_company_id",
                table: "module_external_connections");

            migrationBuilder.DropIndex(
                name: "uq_module_external_connections_company_module",
                table: "module_external_connections");

            migrationBuilder.AlterColumn<Guid>(
                name: "company_id",
                table: "module_external_connections",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "module_external_connections",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_module_external_connections_company_id",
                table: "module_external_connections",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_module_external_connections_org_company_module",
                table: "module_external_connections",
                columns: new[] { "organization_id", "company_id", "module_code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_module_external_connections_company_company_id",
                table: "module_external_connections",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_module_external_connections_organizations_organization_id",
                table: "module_external_connections",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
