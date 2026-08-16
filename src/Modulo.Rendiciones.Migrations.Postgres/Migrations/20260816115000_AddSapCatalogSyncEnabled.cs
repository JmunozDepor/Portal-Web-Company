using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Rendiciones.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSapCatalogSyncEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "sap_catalog_sync_enabled",
                table: "rendiciones_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sap_catalog_sync_enabled",
                table: "rendiciones_settings");
        }
    }
}
