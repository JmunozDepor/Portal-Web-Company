using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ImportFormatScreenHeader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ship_to_code",
                table: "generic_import_jobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "warehouse_code",
                table: "generic_import_jobs",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "branch_source",
                table: "generic_import_configs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Mapping");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ship_to_code",
                table: "generic_import_jobs");

            migrationBuilder.DropColumn(
                name: "warehouse_code",
                table: "generic_import_jobs");

            migrationBuilder.DropColumn(
                name: "branch_source",
                table: "generic_import_configs");
        }
    }
}
