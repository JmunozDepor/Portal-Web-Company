using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class FaseBTraslados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "lpn",
                table: "manifest_documents",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sap_writeback_error",
                table: "manifest_documents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sap_writeback_status",
                table: "manifest_documents",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "internal_dispatch_type",
                table: "despachos_settings",
                type: "int",
                nullable: true,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "internal_transfer_indicator",
                table: "despachos_settings",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "lpn_pattern",
                table: "despachos_settings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transfer_dispatch_date_field",
                table: "despachos_settings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transfer_folio_prefix",
                table: "despachos_settings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true,
                defaultValue: "52");

            migrationBuilder.AddColumn<string>(
                name: "transfer_manifest_field",
                table: "despachos_settings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "despachos_branch_warehouses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    warehouse_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "nvarchar(70)", maxLength: 70, nullable: true),
                    commune_name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_despachos_branch_warehouses", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_manifest_documents_company_id_document_kind_sap_doc_entry",
                table: "manifest_documents",
                columns: new[] { "company_id", "document_kind", "sap_doc_entry" });

            migrationBuilder.CreateIndex(
                name: "ix_despachos_branch_warehouses_company_id",
                table: "despachos_branch_warehouses",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_despachos_branch_warehouses_company_id_warehouse_code",
                table: "despachos_branch_warehouses",
                columns: new[] { "company_id", "warehouse_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "despachos_branch_warehouses");

            migrationBuilder.DropIndex(
                name: "ix_manifest_documents_company_id_document_kind_sap_doc_entry",
                table: "manifest_documents");

            migrationBuilder.DropColumn(
                name: "lpn",
                table: "manifest_documents");

            migrationBuilder.DropColumn(
                name: "sap_writeback_error",
                table: "manifest_documents");

            migrationBuilder.DropColumn(
                name: "sap_writeback_status",
                table: "manifest_documents");

            migrationBuilder.DropColumn(
                name: "internal_dispatch_type",
                table: "despachos_settings");

            migrationBuilder.DropColumn(
                name: "internal_transfer_indicator",
                table: "despachos_settings");

            migrationBuilder.DropColumn(
                name: "lpn_pattern",
                table: "despachos_settings");

            migrationBuilder.DropColumn(
                name: "transfer_dispatch_date_field",
                table: "despachos_settings");

            migrationBuilder.DropColumn(
                name: "transfer_folio_prefix",
                table: "despachos_settings");

            migrationBuilder.DropColumn(
                name: "transfer_manifest_field",
                table: "despachos_settings");
        }
    }
}
