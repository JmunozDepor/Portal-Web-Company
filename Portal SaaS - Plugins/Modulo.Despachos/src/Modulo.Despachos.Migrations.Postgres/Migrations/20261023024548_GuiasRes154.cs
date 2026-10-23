using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modulo.Despachos.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class GuiasRes154 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "dispatch_type",
                table: "despachos_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "guides_enabled",
                table: "despachos_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "transfer_indicator",
                table: "despachos_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "transport_guides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    emission_key = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_tax_id = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    receiver_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    destination_address = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    destination_commune = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    destination_city = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    transfer_indicator = table.Column<int>(type: "integer", nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    total_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    folio = table.Column<long>(type: "bigint", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_guides", x => x.id);
                    table.ForeignKey(
                        name: "fk_transport_guides_manifests",
                        column: x => x.manifest_id,
                        principalTable: "manifests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transport_guide_documents",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transport_guide_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manifest_document_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_guide_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_transport_guide_documents_manifest_documents",
                        column: x => x.manifest_document_id,
                        principalTable: "manifest_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transport_guide_documents_transport_guides",
                        column: x => x.transport_guide_id,
                        principalTable: "transport_guides",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transport_guide_documents_company_id",
                table: "transport_guide_documents",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_transport_guide_documents_manifest_document_id",
                table: "transport_guide_documents",
                column: "manifest_document_id");

            migrationBuilder.CreateIndex(
                name: "uq_transport_guide_documents_guide_id_document_id",
                table: "transport_guide_documents",
                columns: new[] { "transport_guide_id", "manifest_document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transport_guides_company_id",
                table: "transport_guides",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_transport_guides_emission_key",
                table: "transport_guides",
                column: "emission_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_transport_guides_manifest_id_sequence",
                table: "transport_guides",
                columns: new[] { "manifest_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transport_guide_documents");

            migrationBuilder.DropTable(
                name: "transport_guides");

            migrationBuilder.DropColumn(
                name: "dispatch_type",
                table: "despachos_settings");

            migrationBuilder.DropColumn(
                name: "guides_enabled",
                table: "despachos_settings");

            migrationBuilder.DropColumn(
                name: "transfer_indicator",
                table: "despachos_settings");
        }
    }
}
