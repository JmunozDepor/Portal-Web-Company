using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ImportFormatWarehouseAndSalesPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sales_person_code",
                table: "generic_import_configs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sales_person_mode",
                table: "generic_import_configs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "BusinessPartner");

            migrationBuilder.AddColumn<string>(
                name: "warehouse_source",
                table: "generic_import_configs",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Mapping");

            migrationBuilder.AddColumn<string>(
                name: "warehouse_user_field",
                table: "generic_import_configs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sales_person_code",
                table: "generic_import_configs");

            migrationBuilder.DropColumn(
                name: "sales_person_mode",
                table: "generic_import_configs");

            migrationBuilder.DropColumn(
                name: "warehouse_source",
                table: "generic_import_configs");

            migrationBuilder.DropColumn(
                name: "warehouse_user_field",
                table: "generic_import_configs");
        }
    }
}
