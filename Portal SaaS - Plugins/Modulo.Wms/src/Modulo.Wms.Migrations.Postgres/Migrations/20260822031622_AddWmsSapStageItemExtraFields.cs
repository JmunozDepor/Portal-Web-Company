using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.Wms.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsSapStageItemExtraFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "extra_fields",
                table: "wms_sap_stage_item",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "extra_fields",
                table: "wms_sap_stage_item");
        }
    }
}
