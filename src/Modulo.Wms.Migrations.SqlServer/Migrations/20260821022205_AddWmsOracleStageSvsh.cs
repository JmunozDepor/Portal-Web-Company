using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Wms.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsOracleStageSvsh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wms_oracle_stage_svsh",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    parent_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    error_msg = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    retry_count = table.Column<int>(type: "int", nullable: false),
                    sap_doc_entry = table.Column<int>(type: "int", nullable: true),
                    sap_object = table.Column<int>(type: "int", nullable: true),
                    document_version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    origin_system = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    client_env_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    parent_company_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    entity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    time_stamp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    message_id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    shipment_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    manifest_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    load_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    facility_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    company_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    asn_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    carrier_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    trailer_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    seal_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    rcvd_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    rcvd_date_time = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    cust_nbr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    vendor_nbr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    customer_po_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    shipment_dtl_cust_field_1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    shipment_dtl_cust_field_2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    shipment_dtl_cust_field_3 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    item_part_a = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    item_part_b = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    item_alternate_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    received_qty = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    shipped_qty = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    shipped_uom = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ib_lpn_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    batch_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    expiry_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    serial_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    line_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    seq_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_oracle_stage_svsh", x => x.line_id);
                    table.ForeignKey(
                        name: "FK_wms_oracle_stage_svsh_wms_oracle_inbound_stage_parent_id",
                        column: x => x.parent_id,
                        principalTable: "wms_oracle_inbound_stage",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wms_oracle_stage_svsh_parent",
                table: "wms_oracle_stage_svsh",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_wms_oracle_stage_svsh_shipment",
                table: "wms_oracle_stage_svsh",
                column: "shipment_nbr");

            migrationBuilder.CreateIndex(
                name: "ix_wms_oracle_stage_svsh_status_retry",
                table: "wms_oracle_stage_svsh",
                columns: new[] { "status", "retry_count" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_oracle_stage_svsh");
        }
    }
}
