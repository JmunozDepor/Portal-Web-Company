using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationScopedMenuGroupsAndProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "profiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                table: "menu_groups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "menu_groups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "organization_module_visibilities",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    module_id = table.Column<long>(type: "bigint", nullable: false),
                    is_hidden = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_module_visibilities", x => new { x.organization_id, x.module_id });
                    table.ForeignKey(
                        name: "fk_organization_module_visibilities_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_organization_module_visibilities_platform_modules_module_id",
                        column: x => x.module_id,
                        principalTable: "platform_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profiles_organization_id",
                table: "profiles",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_groups_company_id",
                table: "menu_groups",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_groups_organization_id",
                table: "menu_groups",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_module_visibilities_module_id",
                table: "organization_module_visibilities",
                column: "module_id");

            migrationBuilder.AddForeignKey(
                name: "fk_menu_groups_companies_company_id",
                table: "menu_groups",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_menu_groups_organizations_organization_id",
                table: "menu_groups",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_profiles_organizations_organization_id",
                table: "profiles",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_menu_groups_companies_company_id",
                table: "menu_groups");

            migrationBuilder.DropForeignKey(
                name: "fk_menu_groups_organizations_organization_id",
                table: "menu_groups");

            migrationBuilder.DropForeignKey(
                name: "fk_profiles_organizations_organization_id",
                table: "profiles");

            migrationBuilder.DropTable(
                name: "organization_module_visibilities");

            migrationBuilder.DropIndex(
                name: "ix_profiles_organization_id",
                table: "profiles");

            migrationBuilder.DropIndex(
                name: "ix_menu_groups_company_id",
                table: "menu_groups");

            migrationBuilder.DropIndex(
                name: "ix_menu_groups_organization_id",
                table: "menu_groups");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "company_id",
                table: "menu_groups");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "menu_groups");
        }
    }
}
