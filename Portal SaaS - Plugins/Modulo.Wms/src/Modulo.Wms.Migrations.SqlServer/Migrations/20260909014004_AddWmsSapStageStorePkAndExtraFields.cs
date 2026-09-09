using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Wms.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsSapStageStorePkAndExtraFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "card_name",
                table: "wms_sap_stage_store");

            migrationBuilder.DropColumn(
                name: "city",
                table: "wms_sap_stage_store");

            migrationBuilder.DropColumn(
                name: "street",
                table: "wms_sap_stage_store");

            migrationBuilder.DropColumn(
                name: "zip_code",
                table: "wms_sap_stage_store");

            migrationBuilder.RenameColumn(
                name: "card_code",
                table: "wms_sap_stage_store",
                newName: "pk");

            migrationBuilder.RenameIndex(
                name: "ix_wms_sap_stage_store_company_cardcode",
                table: "wms_sap_stage_store",
                newName: "ix_wms_sap_stage_store_company_pk");

            migrationBuilder.AddColumn<string>(
                name: "extra_fields",
                table: "wms_sap_stage_store",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "extra_fields",
                table: "wms_sap_stage_store");

            migrationBuilder.RenameColumn(
                name: "pk",
                table: "wms_sap_stage_store",
                newName: "card_code");

            migrationBuilder.RenameIndex(
                name: "ix_wms_sap_stage_store_company_pk",
                table: "wms_sap_stage_store",
                newName: "ix_wms_sap_stage_store_company_cardcode");

            migrationBuilder.AddColumn<string>(
                name: "card_name",
                table: "wms_sap_stage_store",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "wms_sap_stage_store",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "street",
                table: "wms_sap_stage_store",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "zip_code",
                table: "wms_sap_stage_store",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }
    }
}
