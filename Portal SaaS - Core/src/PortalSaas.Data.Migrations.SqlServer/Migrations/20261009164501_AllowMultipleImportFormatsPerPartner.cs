using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleImportFormatsPerPartner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_generic_import_configs_company_id_module_document_type_line_type_business_partner_card_code",
                table: "generic_import_configs");

            migrationBuilder.CreateIndex(
                name: "ix_generic_import_configs_company_id_module_document_type_line_type_business_partner_card_code_alias",
                table: "generic_import_configs",
                columns: new[] { "company_id", "module", "document_type", "line_type", "business_partner_card_code", "alias" },
                unique: true,
                filter: "[business_partner_card_code] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_generic_import_configs_company_id_module_document_type_line_type_business_partner_card_code_alias",
                table: "generic_import_configs");

            migrationBuilder.CreateIndex(
                name: "ix_generic_import_configs_company_id_module_document_type_line_type_business_partner_card_code",
                table: "generic_import_configs",
                columns: new[] { "company_id", "module", "document_type", "line_type", "business_partner_card_code" },
                unique: true,
                filter: "[business_partner_card_code] IS NOT NULL");
        }
    }
}
