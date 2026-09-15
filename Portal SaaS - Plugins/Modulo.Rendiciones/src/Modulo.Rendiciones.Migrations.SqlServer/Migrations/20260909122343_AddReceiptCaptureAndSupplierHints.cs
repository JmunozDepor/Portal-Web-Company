using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Rendiciones.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptCaptureAndSupplierHints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "capture_source",
                table: "expense_report_lines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "exempt_amount",
                table: "expense_report_lines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "net_amount",
                table: "expense_report_lines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "transaction_time",
                table: "expense_report_lines",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sii_code",
                table: "document_types",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "supplier_hints",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_tax_id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    supplier_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    default_expense_type_id = table.Column<long>(type: "bigint", nullable: true),
                    times_category_confirmed = table.Column<int>(type: "int", nullable: false),
                    default_document_type_id = table.Column<long>(type: "bigint", nullable: true),
                    category_counts = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    times_seen = table.Column<int>(type: "int", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_hints", x => x.id);
                    table.ForeignKey(
                        name: "fk_supplier_hints_document_types",
                        column: x => x.default_document_type_id,
                        principalTable: "document_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_supplier_hints_expense_types",
                        column: x => x.default_expense_type_id,
                        principalTable: "expense_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_hints_default_document_type_id",
                table: "supplier_hints",
                column: "default_document_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_hints_default_expense_type_id",
                table: "supplier_hints",
                column: "default_expense_type_id");

            migrationBuilder.CreateIndex(
                name: "uq_supplier_hints_company_id_supplier_tax_id",
                table: "supplier_hints",
                columns: new[] { "company_id", "supplier_tax_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_hints");

            migrationBuilder.DropColumn(
                name: "capture_source",
                table: "expense_report_lines");

            migrationBuilder.DropColumn(
                name: "exempt_amount",
                table: "expense_report_lines");

            migrationBuilder.DropColumn(
                name: "net_amount",
                table: "expense_report_lines");

            migrationBuilder.DropColumn(
                name: "transaction_time",
                table: "expense_report_lines");

            migrationBuilder.DropColumn(
                name: "sii_code",
                table: "document_types");
        }
    }
}
