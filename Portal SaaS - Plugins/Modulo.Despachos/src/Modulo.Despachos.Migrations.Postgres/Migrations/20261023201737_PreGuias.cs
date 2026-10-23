using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modulo.Despachos.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PreGuias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "dispatch_group_id",
                table: "manifest_documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "despachos_dispatch_groups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    number = table.Column<int>(type: "integer", nullable: false),
                    warehouse_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    branch_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dispatch_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    annul_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_despachos_dispatch_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "despachos_dispatch_group_transfers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispatch_group_id = table.Column<long>(type: "bigint", nullable: false),
                    sap_doc_entry = table.Column<int>(type: "integer", nullable: false),
                    sap_doc_num = table.Column<int>(type: "integer", nullable: false),
                    lpn = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    units = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_despachos_dispatch_group_transfers", x => x.id);
                    table.ForeignKey(
                        name: "FK_despachos_dispatch_group_transfers_despachos_dispatch_group~",
                        column: x => x.dispatch_group_id,
                        principalTable: "despachos_dispatch_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_manifest_documents_dispatch_group_id",
                table: "manifest_documents",
                column: "dispatch_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_despachos_dispatch_group_transfers_company_id_sap_doc_entry",
                table: "despachos_dispatch_group_transfers",
                columns: new[] { "company_id", "sap_doc_entry" });

            migrationBuilder.CreateIndex(
                name: "IX_despachos_dispatch_group_transfers_dispatch_group_id",
                table: "despachos_dispatch_group_transfers",
                column: "dispatch_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_despachos_dispatch_groups_company_id",
                table: "despachos_dispatch_groups",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_despachos_dispatch_groups_company_id_dispatch_date",
                table: "despachos_dispatch_groups",
                columns: new[] { "company_id", "dispatch_date" });

            migrationBuilder.CreateIndex(
                name: "uq_despachos_dispatch_groups_company_id_number",
                table: "despachos_dispatch_groups",
                columns: new[] { "company_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "despachos_dispatch_group_transfers");

            migrationBuilder.DropTable(
                name: "despachos_dispatch_groups");

            migrationBuilder.DropIndex(
                name: "ix_manifest_documents_dispatch_group_id",
                table: "manifest_documents");

            migrationBuilder.DropColumn(
                name: "dispatch_group_id",
                table: "manifest_documents");
        }
    }
}
