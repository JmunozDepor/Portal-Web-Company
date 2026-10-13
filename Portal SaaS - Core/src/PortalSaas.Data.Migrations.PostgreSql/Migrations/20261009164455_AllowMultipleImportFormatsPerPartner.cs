using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleImportFormatsPerPartner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_generic_import_configs_company_id_module_document_type_line",
                table: "generic_import_configs");

            migrationBuilder.CreateIndex(
                name: "ix_generic_import_configs_company_id_module_document_type_line",
                table: "generic_import_configs",
                columns: new[] { "company_id", "module", "document_type", "line_type", "business_partner_card_code", "alias" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_generic_import_configs_company_id_module_document_type_line",
                table: "generic_import_configs");

            migrationBuilder.CreateIndex(
                name: "ix_generic_import_configs_company_id_module_document_type_line",
                table: "generic_import_configs",
                columns: new[] { "company_id", "module", "document_type", "line_type", "business_partner_card_code" },
                unique: true);
        }
    }
}
