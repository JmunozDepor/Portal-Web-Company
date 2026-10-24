using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modulo.Despachos.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class GuiasManuales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_transport_guides_manifest_id_sequence",
                table: "transport_guides");

            migrationBuilder.AlterColumn<int>(
                name: "sequence",
                table: "transport_guides",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "manifest_id",
                table: "transport_guides",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "annul_reason",
                table: "transport_guides",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                table: "transport_guides",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dispatch_type",
                table: "transport_guides",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "issue_date",
                table: "transport_guides",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "issued_by",
                table: "transport_guides",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "net_amount",
                table: "transport_guides",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "transport_guides",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "number",
                table: "transport_guides",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "origin_id",
                table: "transport_guides",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receiver_card_code",
                table: "transport_guides",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "transport_guides",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "manifest");

            migrationBuilder.AddColumn<decimal>(
                name: "total_amount",
                table: "transport_guides",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transport_mode",
                table: "transport_guides",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "vat_amount",
                table: "transport_guides",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "transport_guide_id",
                table: "manifest_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "transfer_motives",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    vat_affected = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_motives", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_transport_guides_company_id_number",
                table: "transport_guides",
                columns: new[] { "company_id", "number" },
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_transport_guides_manifest_id_sequence",
                table: "transport_guides",
                columns: new[] { "manifest_id", "sequence" },
                unique: true,
                filter: "manifest_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_manifest_documents_transport_guide_id",
                table: "manifest_documents",
                column: "transport_guide_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_motives_company_id",
                table: "transfer_motives",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_transfer_motives_company_id_code",
                table: "transfer_motives",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_manifest_documents_transport_guides",
                table: "manifest_documents",
                column: "transport_guide_id",
                principalTable: "transport_guides",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_manifest_documents_transport_guides",
                table: "manifest_documents");

            migrationBuilder.DropTable(
                name: "transfer_motives");

            migrationBuilder.DropIndex(
                name: "uq_transport_guides_company_id_number",
                table: "transport_guides");

            migrationBuilder.DropIndex(
                name: "uq_transport_guides_manifest_id_sequence",
                table: "transport_guides");

            migrationBuilder.DropIndex(
                name: "ix_manifest_documents_transport_guide_id",
                table: "manifest_documents");

            migrationBuilder.DropColumn(
                name: "annul_reason",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "created_by",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "dispatch_type",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "issue_date",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "issued_by",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "net_amount",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "number",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "origin_id",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "receiver_card_code",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "source",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "total_amount",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "transport_mode",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "vat_amount",
                table: "transport_guides");

            migrationBuilder.DropColumn(
                name: "transport_guide_id",
                table: "manifest_documents");

            migrationBuilder.AlterColumn<int>(
                name: "sequence",
                table: "transport_guides",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "manifest_id",
                table: "transport_guides",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_transport_guides_manifest_id_sequence",
                table: "transport_guides",
                columns: new[] { "manifest_id", "sequence" },
                unique: true);
        }
    }
}
