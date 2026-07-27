using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformModuleExclusiveOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "exclusive_organization_id",
                table: "platform_modules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_modules_exclusive_organization_id",
                table: "platform_modules",
                column: "exclusive_organization_id");

            migrationBuilder.AddForeignKey(
                name: "fk_platform_modules_organizations_exclusive_organization_id",
                table: "platform_modules",
                column: "exclusive_organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_platform_modules_organizations_exclusive_organization_id",
                table: "platform_modules");

            migrationBuilder.DropIndex(
                name: "ix_platform_modules_exclusive_organization_id",
                table: "platform_modules");

            migrationBuilder.DropColumn(
                name: "exclusive_organization_id",
                table: "platform_modules");
        }
    }
}
