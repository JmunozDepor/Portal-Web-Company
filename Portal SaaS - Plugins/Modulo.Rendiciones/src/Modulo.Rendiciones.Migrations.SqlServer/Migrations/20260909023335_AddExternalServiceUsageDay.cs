using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Rendiciones.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalServiceUsageDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_external_service_usages_provider_year_month",
                table: "external_service_usages");

            migrationBuilder.AddColumn<int>(
                name: "day",
                table: "external_service_usages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "uq_external_service_usages_provider_year_month_day",
                table: "external_service_usages",
                columns: new[] { "provider_id", "year", "month", "day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_external_service_usages_provider_year_month_day",
                table: "external_service_usages");

            migrationBuilder.DropColumn(
                name: "day",
                table: "external_service_usages");

            migrationBuilder.CreateIndex(
                name: "uq_external_service_usages_provider_year_month",
                table: "external_service_usages",
                columns: new[] { "provider_id", "year", "month" },
                unique: true);
        }
    }
}
