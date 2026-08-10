using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessPartnerFromFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "business_partner_from_file",
                table: "generic_import_configs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "business_partner_from_file",
                table: "generic_import_configs");
        }
    }
}
