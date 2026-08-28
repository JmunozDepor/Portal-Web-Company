using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddSqlConnectorType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_integration_definitions_connector_type",
                table: "integration_definitions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_integration_definitions_connector_type",
                table: "integration_definitions",
                sql: "connector_type in ('sap', 'rest', 'file', 'wmscloud', 'sql')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_integration_definitions_connector_type",
                table: "integration_definitions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_integration_definitions_connector_type",
                table: "integration_definitions",
                sql: "connector_type in ('sap', 'rest', 'file', 'wmscloud')");
        }
    }
}
