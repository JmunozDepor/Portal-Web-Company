using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modulo.Wms.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsOrderStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wms_sap_stage_order_hdr",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_nbr = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pick_list_abs_entry = table.Column<int>(type: "integer", nullable: false),
                    base_object_type = table.Column<int>(type: "integer", nullable: false),
                    base_entry = table.Column<int>(type: "integer", nullable: false),
                    card_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    card_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_po_nbr = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ord_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    exp_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    req_ship_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ship_to_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    source_update_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    error_msg = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_sap_stage_order_hdr", x => x.line_id);
                });

            migrationBuilder.CreateTable(
                name: "wms_sap_stage_order_dtl",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    parent_id = table.Column<long>(type: "bigint", nullable: false),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    whs_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    line_num = table.Column<int>(type: "integer", nullable: false),
                    seq_nbr = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wms_sap_stage_order_dtl", x => x.line_id);
                    table.ForeignKey(
                        name: "FK_wms_sap_stage_order_dtl_wms_sap_stage_order_hdr_parent_id",
                        column: x => x.parent_id,
                        principalTable: "wms_sap_stage_order_hdr",
                        principalColumn: "line_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wms_sap_stage_order_dtl_parent_id",
                table: "wms_sap_stage_order_dtl",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_wms_sap_stage_order_hdr_company_ordernbr",
                table: "wms_sap_stage_order_hdr",
                columns: new[] { "company_id", "order_nbr" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wms_sap_stage_order_hdr_status",
                table: "wms_sap_stage_order_hdr",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wms_sap_stage_order_dtl");

            migrationBuilder.DropTable(
                name: "wms_sap_stage_order_hdr");
        }
    }
}
