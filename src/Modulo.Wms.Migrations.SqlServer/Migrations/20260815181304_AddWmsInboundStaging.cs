using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Wms.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsInboundStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wms_oracle_inbound_stage",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tipo_doc = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    formato = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    nombre_archivo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    hash_archivo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    contenido = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    intentos = table.Column<int>(type: "int", nullable: false),
                    mensaje_error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    sap_doc_entry = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    inserted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_oracle_inbound_stage", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wms_oracle_stage_slsh",
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
                    facility_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    company_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    action_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    load_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    load_manifest_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    trailer_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    trailer_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    driver = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    seal_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    pro_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    route_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    freight_class = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    hdr_bol_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    total_nbr_of_oblpns = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    total_weight = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    total_volume = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    total_shipping_charge = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ship_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    sched_delivery_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    carrier_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    externally_planned_load_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ship_date_time = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    sched_delivery_date_time = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    line_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    seq_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    stop_shipment_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    stop_bol_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    stop_nbr_of_oblpns = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    stop_weight = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    stop_volume = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    stop_shipping_charge = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    shipto_facility_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    shipto_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    shipto_addr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    shipto_addr2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    shipto_addr3 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    shipto_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    shipto_state = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    shipto_zip = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    shipto_country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    shipto_phone_nbr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    shipto_email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    shipto_contact = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    dest_facility_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_addr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_addr2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_addr3 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_state = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_zip = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_phone_nbr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_contact = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    cust_nbr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ord_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    exp_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    req_ship_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    start_ship_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    stop_ship_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    host_allocation_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    customer_po_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    sales_order_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    sales_channel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    dest_dept_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_field_1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_hdr_cust_field_2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_hdr_cust_field_3 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_hdr_cust_field_4 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_hdr_cust_field_5 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_seq_nbr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_dtl_cust_field_1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_dtl_cust_field_2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_dtl_cust_field_3 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_dtl_cust_field_4 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_dtl_cust_field_5 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ob_lpn_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    item_alternate_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    item_part_a = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    item_part_b = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    item_part_c = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    item_part_d = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    item_part_e = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    item_part_f = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    pre_pack_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    pre_pack_ratio = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    pre_pack_ratio_seq = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    pre_pack_total_units = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    invn_attr_a = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    invn_attr_b = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    invn_attr_c = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    hazmat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    shipped_uom = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    shipped_qty = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    pallet_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    dest_company_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    batch_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    expiry_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    tracking_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    master_tracking_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    package_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    payment_method = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    carrier_account_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ship_via_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ob_lpn_weight = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ob_lpn_volume = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ob_lpn_shipping_charge = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ob_lpn_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ob_lpn_asset_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ob_lpn_asset_seal_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    serial_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    customer_po_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    customer_vendor_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_date_1 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_date_2 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_date_3 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_date_4 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_date_5 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_number_1 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_number_2 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_number_3 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_number_4 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_number_5 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_decimal_1 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_decimal_2 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_decimal_3 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_decimal_4 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_decimal_5 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_hdr_cust_short_text_1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_short_text_2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_short_text_3 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_short_text_4 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_short_text_5 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_short_text_6 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_short_text_7 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_short_text_8 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_short_text_9 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_short_text_10 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_short_text_11 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_short_text_12 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_long_text_1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_long_text_2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_hdr_cust_long_text_3 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_date_1 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_date_2 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_date_3 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_date_4 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_date_5 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_number_1 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_number_2 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_number_3 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_number_4 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_number_5 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_decimal_1 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_decimal_2 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_decimal_3 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_decimal_4 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_decimal_5 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    order_dtl_cust_short_text_1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_short_text_2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_short_text_3 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_short_text_4 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_short_text_5 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_short_text_6 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_short_text_7 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_short_text_8 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_short_text_9 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_short_text_10 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_short_text_11 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_short_text_12 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_long_text_1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_long_text_2 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_dtl_cust_long_text_3 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    invn_attr_d = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    invn_attr_e = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    invn_attr_f = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    invn_attr_g = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    order_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    rcvd_trailer_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    stop_seal_nbr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ship_request_line = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_oracle_stage_slsh", x => x.line_id);
                    table.ForeignKey(
                        name: "FK_wms_oracle_stage_slsh_wms_oracle_inbound_stage_parent_id",
                        column: x => x.parent_id,
                        principalTable: "wms_oracle_inbound_stage",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wms_oracle_inbound_stage_company_hash",
                table: "wms_oracle_inbound_stage",
                columns: new[] { "company_id", "hash_archivo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wms_oracle_inbound_stage_estado",
                table: "wms_oracle_inbound_stage",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_wms_oracle_stage_slsh_lpn",
                table: "wms_oracle_stage_slsh",
                column: "ob_lpn_nbr");

            migrationBuilder.CreateIndex(
                name: "ix_wms_oracle_stage_slsh_parent",
                table: "wms_oracle_stage_slsh",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_wms_oracle_stage_slsh_status_retry",
                table: "wms_oracle_stage_slsh",
                columns: new[] { "status", "retry_count" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_oracle_stage_slsh");

            migrationBuilder.DropTable(
                name: "wms_oracle_inbound_stage");
        }
    }
}
