using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddDteReception : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reception_base_url",
                table: "company_dte_settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "reception_enabled",
                table: "company_dte_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "reception_initial_days",
                table: "company_dte_settings",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reception_initial_load_done_at",
                table: "company_dte_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reception_interval_minutes",
                table: "company_dte_settings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<string>(
                name: "reception_last_error",
                table: "company_dte_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reception_last_sync_at",
                table: "company_dte_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reception_user_mail",
                table: "company_dte_settings",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reception_window_days",
                table: "company_dte_settings",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.CreateTable(
                name: "dte_received",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issuer_tax_id = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    issuer_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dte_type = table.Column<int>(type: "integer", nullable: false),
                    folio = table.Column<long>(type: "bigint", nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sii_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    net_amount = table.Column<long>(type: "bigint", nullable: false),
                    exempt_amount = table.Column<long>(type: "bigint", nullable: false),
                    vat_amount = table.Column<long>(type: "bigint", nullable: false),
                    other_taxes_json = table.Column<string>(type: "text", nullable: true),
                    total_amount = table.Column<long>(type: "bigint", nullable: false),
                    sii_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    commercial_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    claim_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_assigned = table.Column<bool>(type: "boolean", nullable: false),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false),
                    provider_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    references_json = table.Column<string>(type: "text", nullable: false),
                    purchase_order_refs_json = table.Column<string>(type: "text", nullable: false),
                    xml = table.Column<string>(type: "text", nullable: true),
                    response_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    response_action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    response_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    response_actor = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    response_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    response_attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    processing_module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    processing_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    processing_ref = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    processing_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dte_received", x => x.id);
                    table.CheckConstraint("ck_dte_received_response_status", "response_status in ('none', 'pending', 'sent', 'failed', 'external')");
                    table.ForeignKey(
                        name: "fk_dte_received_company_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dte_received_company_id_sii_received_at",
                table: "dte_received",
                columns: new[] { "company_id", "sii_received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_dte_received_company_id_updated_at",
                table: "dte_received",
                columns: new[] { "company_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "uq_dte_received_company_document",
                table: "dte_received",
                columns: new[] { "company_id", "issuer_tax_id", "dte_type", "folio" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dte_received");

            migrationBuilder.DropColumn(
                name: "reception_base_url",
                table: "company_dte_settings");

            migrationBuilder.DropColumn(
                name: "reception_enabled",
                table: "company_dte_settings");

            migrationBuilder.DropColumn(
                name: "reception_initial_days",
                table: "company_dte_settings");

            migrationBuilder.DropColumn(
                name: "reception_initial_load_done_at",
                table: "company_dte_settings");

            migrationBuilder.DropColumn(
                name: "reception_interval_minutes",
                table: "company_dte_settings");

            migrationBuilder.DropColumn(
                name: "reception_last_error",
                table: "company_dte_settings");

            migrationBuilder.DropColumn(
                name: "reception_last_sync_at",
                table: "company_dte_settings");

            migrationBuilder.DropColumn(
                name: "reception_user_mail",
                table: "company_dte_settings");

            migrationBuilder.DropColumn(
                name: "reception_window_days",
                table: "company_dte_settings");
        }
    }
}
