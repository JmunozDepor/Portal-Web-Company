using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Rendiciones.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddExpensePolicyCityProfilePeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_blocking",
                table: "expense_policies");

            migrationBuilder.AddColumn<long>(
                name: "city_id",
                table: "expense_policies",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "period",
                table: "expense_policies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PerDocument");

            migrationBuilder.AddColumn<long>(
                name: "policy_profile_id",
                table: "expense_policies",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "city_key",
                table: "expense_policies",
                type: "bigint",
                nullable: false,
                computedColumnSql: "COALESCE(city_id, 0)",
                stored: true);

            migrationBuilder.AddColumn<long>(
                name: "profile_key",
                table: "expense_policies",
                type: "bigint",
                nullable: false,
                computedColumnSql: "COALESCE(policy_profile_id, 0)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_expense_policies_city_id",
                table: "expense_policies",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_policies_policy_profile_id",
                table: "expense_policies",
                column: "policy_profile_id");

            migrationBuilder.CreateIndex(
                name: "uq_expense_policies_company_type_city_profile",
                table: "expense_policies",
                columns: new[] { "company_id", "expense_type_id", "city_key", "profile_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_expense_policies_expense_cities",
                table: "expense_policies",
                column: "city_id",
                principalTable: "expense_cities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_expense_policies_policy_profiles",
                table: "expense_policies",
                column: "policy_profile_id",
                principalTable: "policy_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_expense_policies_expense_cities",
                table: "expense_policies");

            migrationBuilder.DropForeignKey(
                name: "fk_expense_policies_policy_profiles",
                table: "expense_policies");

            migrationBuilder.DropIndex(
                name: "IX_expense_policies_city_id",
                table: "expense_policies");

            migrationBuilder.DropIndex(
                name: "IX_expense_policies_policy_profile_id",
                table: "expense_policies");

            migrationBuilder.DropIndex(
                name: "uq_expense_policies_company_type_city_profile",
                table: "expense_policies");

            migrationBuilder.DropColumn(
                name: "city_key",
                table: "expense_policies");

            migrationBuilder.DropColumn(
                name: "profile_key",
                table: "expense_policies");

            migrationBuilder.DropColumn(
                name: "city_id",
                table: "expense_policies");

            migrationBuilder.DropColumn(
                name: "period",
                table: "expense_policies");

            migrationBuilder.DropColumn(
                name: "policy_profile_id",
                table: "expense_policies");

            migrationBuilder.AddColumn<bool>(
                name: "is_blocking",
                table: "expense_policies",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
