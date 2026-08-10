using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyScopeToGenericImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_generic_import_configs_organizations_organization_id",
                table: "generic_import_configs");

            migrationBuilder.DropForeignKey(
                name: "fk_generic_import_user_fields_organizations_organization_id",
                table: "generic_import_user_fields");

            migrationBuilder.RenameColumn(
                name: "organization_id",
                table: "generic_import_user_fields",
                newName: "company_id");

            migrationBuilder.RenameIndex(
                name: "ix_generic_import_user_fields_organization_id",
                table: "generic_import_user_fields",
                newName: "ix_generic_import_user_fields_company_id");

            migrationBuilder.RenameColumn(
                name: "organization_id",
                table: "generic_import_configs",
                newName: "company_id");

            migrationBuilder.RenameIndex(
                name: "ix_generic_import_configs_organization_id_module_document_type_line_type_business_partner_card_code",
                table: "generic_import_configs",
                newName: "ix_generic_import_configs_company_id_module_document_type_line_type_business_partner_card_code");

            migrationBuilder.AddForeignKey(
                name: "fk_generic_import_configs_company_company_id",
                table: "generic_import_configs",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_generic_import_user_fields_company_company_id",
                table: "generic_import_user_fields",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_generic_import_configs_company_company_id",
                table: "generic_import_configs");

            migrationBuilder.DropForeignKey(
                name: "fk_generic_import_user_fields_company_company_id",
                table: "generic_import_user_fields");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "generic_import_user_fields",
                newName: "organization_id");

            migrationBuilder.RenameIndex(
                name: "ix_generic_import_user_fields_company_id",
                table: "generic_import_user_fields",
                newName: "ix_generic_import_user_fields_organization_id");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "generic_import_configs",
                newName: "organization_id");

            migrationBuilder.RenameIndex(
                name: "ix_generic_import_configs_company_id_module_document_type_line_type_business_partner_card_code",
                table: "generic_import_configs",
                newName: "ix_generic_import_configs_organization_id_module_document_type_line_type_business_partner_card_code");

            migrationBuilder.AddForeignKey(
                name: "fk_generic_import_configs_organizations_organization_id",
                table: "generic_import_configs",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_generic_import_user_fields_organizations_organization_id",
                table: "generic_import_user_fields",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
