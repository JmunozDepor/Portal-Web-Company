using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Rendiciones.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class RenameExpensePolicyUniqueIndexAddPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_expense_policies_company_type_city_profile",
                table: "expense_policies");

            migrationBuilder.CreateIndex(
                name: "uq_expense_policies_company_type_city_profile_period",
                table: "expense_policies",
                columns: new[] { "company_id", "expense_type_id", "city_key", "profile_key", "period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_expense_policies_company_type_city_profile_period",
                table: "expense_policies");

            migrationBuilder.CreateIndex(
                name: "uq_expense_policies_company_type_city_profile",
                table: "expense_policies",
                columns: new[] { "company_id", "expense_type_id", "city_key", "profile_key" },
                unique: true);
        }
    }
}
