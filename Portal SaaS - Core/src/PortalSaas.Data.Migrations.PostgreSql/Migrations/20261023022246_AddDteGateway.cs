using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddDteGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_dte_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    base_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    secret_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    issuer_tax_id = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    issuer_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    issuer_activity = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    issuer_activity_code = table.Column<int>(type: "integer", nullable: false),
                    issuer_address = table.Column<string>(type: "character varying(70)", maxLength: 70, nullable: false),
                    issuer_commune = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issuer_city = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issuer_sii_branch_code = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_dte_settings", x => x.id);
                    table.CheckConstraint("ck_company_dte_settings_environment", "environment in ('certification', 'production')");
                    table.ForeignKey(
                        name: "fk_company_dte_settings_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dte_emissions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emission_key = table.Column<Guid>(type: "uuid", nullable: false),
                    source_module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dte_type = table.Column<int>(type: "integer", nullable: false),
                    provider_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issuer_tax_id = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    folio = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_json = table.Column<string>(type: "text", nullable: false),
                    xml_sent = table.Column<string>(type: "text", nullable: true),
                    xml_signed = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dte_emissions", x => x.id);
                    table.CheckConstraint("ck_dte_emissions_status", "status in ('pending', 'folio_reserved', 'issued', 'failed', 'released', 'abandoned')");
                    table.ForeignKey(
                        name: "fk_dte_emissions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_company_dte_settings_company",
                table: "company_dte_settings",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dte_emissions_company_id_status_updated_at",
                table: "dte_emissions",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "uq_dte_emissions_company_emission_key",
                table: "dte_emissions",
                columns: new[] { "company_id", "emission_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_dte_settings");

            migrationBuilder.DropTable(
                name: "dte_emissions");
        }
    }
}
