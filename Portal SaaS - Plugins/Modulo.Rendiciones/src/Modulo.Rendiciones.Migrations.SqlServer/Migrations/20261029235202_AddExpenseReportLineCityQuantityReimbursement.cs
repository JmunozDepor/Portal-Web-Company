using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Rendiciones.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseReportLineCityQuantityReimbursement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "city_id",
                table: "expense_report_lines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "quantity",
                table: "expense_report_lines",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reimbursement_amount",
                table: "expense_report_lines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // La columna es NOT NULL y las filas existentes no tienen valor (se agregó con
            // defaultValue 0 arriba) -- hasta que exista el motor de políticas (Task 7) el
            // monto reembolsable es siempre igual al monto total, así que se backfillea acá.
            migrationBuilder.Sql("UPDATE expense_report_lines SET reimbursement_amount = amount");

            migrationBuilder.CreateIndex(
                name: "IX_expense_report_lines_city_id",
                table: "expense_report_lines",
                column: "city_id");

            migrationBuilder.AddForeignKey(
                name: "fk_expense_report_lines_expense_cities",
                table: "expense_report_lines",
                column: "city_id",
                principalTable: "expense_cities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_expense_report_lines_expense_cities",
                table: "expense_report_lines");

            migrationBuilder.DropIndex(
                name: "IX_expense_report_lines_city_id",
                table: "expense_report_lines");

            migrationBuilder.DropColumn(
                name: "city_id",
                table: "expense_report_lines");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "expense_report_lines");

            migrationBuilder.DropColumn(
                name: "reimbursement_amount",
                table: "expense_report_lines");
        }
    }
}
