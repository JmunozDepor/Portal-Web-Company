using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Manifiestos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "despachos_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    issuer_tax_id = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_despachos_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "manifests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    dispatch_date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_id = table.Column<long>(type: "bigint", nullable: true),
                    origin_id = table.Column<long>(type: "bigint", nullable: false),
                    carrier_id = table.Column<long>(type: "bigint", nullable: false),
                    vehicle_id = table.Column<long>(type: "bigint", nullable: false),
                    trailer_id = table.Column<long>(type: "bigint", nullable: true),
                    driver_id = table.Column<long>(type: "bigint", nullable: false),
                    carrier_tax_id = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    driver_tax_id = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    driver_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    plate = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    trailer_plate = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    origin_address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    origin_commune = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    departed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    planned_departure_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    estimated_arrival_date = table.Column<DateOnly>(type: "date", nullable: true),
                    estimated_arrival_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    returned_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    odometer_start = table.Column<int>(type: "int", nullable: true),
                    odometer_end = table.Column<int>(type: "int", nullable: true),
                    service_order = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    cost_calculated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    rate_card_id = table.Column<long>(type: "bigint", nullable: true),
                    cost_override = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    cost_override_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    cost_final = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    cost_frozen_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    cost_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    total_documents = table.Column<int>(type: "int", nullable: false),
                    total_units = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    total_packages = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manifests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "manifest_cost_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    commune_id = table.Column<long>(type: "bigint", nullable: true),
                    rate_card_id = table.Column<long>(type: "bigint", nullable: true),
                    rate_surcharge_id = table.Column<long>(type: "bigint", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    documents = table.Column<int>(type: "int", nullable: false),
                    units = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    packages = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manifest_cost_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_manifest_cost_lines_manifests_manifest_id",
                        column: x => x.manifest_id,
                        principalTable: "manifests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "manifest_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    sii_document_type = table.Column<int>(type: "int", nullable: false),
                    folio = table.Column<long>(type: "bigint", nullable: false),
                    sap_doc_entry = table.Column<int>(type: "int", nullable: false),
                    sap_doc_num = table.Column<int>(type: "int", nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    customer_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    customer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    customer_tax_id = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    destination_address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    destination_commune_name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    destination_commune_id = table.Column<long>(type: "bigint", nullable: true),
                    sap_base_order_entry = table.Column<int>(type: "int", nullable: true),
                    customer_po = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    units = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    packages = table.Column<int>(type: "int", nullable: false),
                    weight_kg = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    volume_m3 = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    delivery_status = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    rejection_reason_id = table.Column<long>(type: "bigint", nullable: true),
                    comments = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    returned_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    returned_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    capture_method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    added_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    added_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manifest_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_manifest_documents_manifests_manifest_id",
                        column: x => x.manifest_id,
                        principalTable: "manifests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "manifest_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    manifest_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    event_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    from_status = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    to_status = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manifest_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_manifest_events_manifests_manifest_id",
                        column: x => x.manifest_id,
                        principalTable: "manifests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_despachos_settings_company_id",
                table: "despachos_settings",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_manifest_cost_lines_company_id",
                table: "manifest_cost_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_manifest_cost_lines_manifest_id",
                table: "manifest_cost_lines",
                column: "manifest_id");

            migrationBuilder.CreateIndex(
                name: "ix_manifest_documents_company_id",
                table: "manifest_documents",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_manifest_documents_company_id_kind_sii_folio",
                table: "manifest_documents",
                columns: new[] { "company_id", "document_kind", "sii_document_type", "folio" });

            migrationBuilder.CreateIndex(
                name: "ix_manifest_documents_manifest_id",
                table: "manifest_documents",
                column: "manifest_id");

            migrationBuilder.CreateIndex(
                name: "ix_manifest_events_company_id",
                table: "manifest_events",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_manifest_events_manifest_id",
                table: "manifest_events",
                column: "manifest_id");

            migrationBuilder.CreateIndex(
                name: "ix_manifests_company_id",
                table: "manifests",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_manifests_company_id_dispatch_date",
                table: "manifests",
                columns: new[] { "company_id", "dispatch_date" });

            migrationBuilder.CreateIndex(
                name: "uq_manifests_company_id_number",
                table: "manifests",
                columns: new[] { "company_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "despachos_settings");

            migrationBuilder.DropTable(
                name: "manifest_cost_lines");

            migrationBuilder.DropTable(
                name: "manifest_documents");

            migrationBuilder.DropTable(
                name: "manifest_events");

            migrationBuilder.DropTable(
                name: "manifests");
        }
    }
}
