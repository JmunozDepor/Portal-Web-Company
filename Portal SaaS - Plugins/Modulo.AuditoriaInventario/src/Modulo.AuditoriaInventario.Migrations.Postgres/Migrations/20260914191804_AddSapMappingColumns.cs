using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.AuditoriaInventario.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSapMappingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sap_material_code",
                table: "products",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sap_company_code",
                table: "branches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sap_warehouse_code",
                table: "branches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sap_material_code",
                table: "products");

            migrationBuilder.DropColumn(
                name: "sap_company_code",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "sap_warehouse_code",
                table: "branches");
        }
    }
}
