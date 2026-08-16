using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Wms.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsSapStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wms_sap_stage_inbound_hdr",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sap_doc_entry = table.Column<int>(type: "int", nullable: false),
                    shipment_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    source_update_date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    retry_count = table.Column<int>(type: "int", nullable: false),
                    error_msg = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_sap_stage_inbound_hdr", x => x.line_id);
                });

            migrationBuilder.CreateTable(
                name: "wms_sap_stage_item",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    item_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    item_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    bar_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    source_update_date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    retry_count = table.Column<int>(type: "int", nullable: false),
                    error_msg = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_sap_stage_item", x => x.line_id);
                });

            migrationBuilder.CreateTable(
                name: "wms_sap_stage_store",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    card_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    card_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    street = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    zip_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    source_update_date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    retry_count = table.Column<int>(type: "int", nullable: false),
                    error_msg = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_sap_stage_store", x => x.line_id);
                });

            migrationBuilder.CreateTable(
                name: "wms_sap_stage_inbound_dtl",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    parent_id = table.Column<long>(type: "bigint", nullable: false),
                    item_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    whs_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    line_num = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_sap_stage_inbound_dtl", x => x.line_id);
                    table.ForeignKey(
                        name: "FK_wms_sap_stage_inbound_dtl_wms_sap_stage_inbound_hdr_parent_id",
                        column: x => x.parent_id,
                        principalTable: "wms_sap_stage_inbound_hdr",
                        principalColumn: "line_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wms_sap_stage_inbound_dtl_parent_id",
                table: "wms_sap_stage_inbound_dtl",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_wms_sap_stage_inbound_hdr_company_docentry",
                table: "wms_sap_stage_inbound_hdr",
                columns: new[] { "company_id", "sap_doc_entry" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wms_sap_stage_inbound_hdr_status",
                table: "wms_sap_stage_inbound_hdr",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_wms_sap_stage_item_company_itemcode",
                table: "wms_sap_stage_item",
                columns: new[] { "company_id", "item_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wms_sap_stage_item_status",
                table: "wms_sap_stage_item",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_wms_sap_stage_store_company_cardcode",
                table: "wms_sap_stage_store",
                columns: new[] { "company_id", "card_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wms_sap_stage_store_status",
                table: "wms_sap_stage_store",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_sap_stage_inbound_dtl");

            migrationBuilder.DropTable(
                name: "wms_sap_stage_item");

            migrationBuilder.DropTable(
                name: "wms_sap_stage_store");

            migrationBuilder.DropTable(
                name: "wms_sap_stage_inbound_hdr");
        }
    }
}
