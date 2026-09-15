using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Rendiciones.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseReportPurposeAndInternalNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "internal_number",
                table: "expense_reports",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "expense_reports",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "internal_number",
                table: "expense_reports");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "expense_reports");
        }
    }
}
