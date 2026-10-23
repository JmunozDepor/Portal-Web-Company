using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Despachos.Migrations.SqlServer.Migrations
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
                        .Annotation("SqlServer:Identity", "1, 1"),
                    number = table.Column<int>(type: "int", nullable: false),
                    warehouse_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    branch_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    dispatch_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    annul_reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
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
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dispatch_group_id = table.Column<long>(type: "bigint", nullable: false),
                    sap_doc_entry = table.Column<int>(type: "int", nullable: false),
                    sap_doc_num = table.Column<int>(type: "int", nullable: false),
                    lpn = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    units = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_despachos_dispatch_group_transfers", x => x.id);
                    table.ForeignKey(
                        name: "FK_despachos_dispatch_group_transfers_despachos_dispatch_groups_dispatch_group_id",
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
