using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuGroupItemDefaultProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "default_profile_id",
                table: "menu_group_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_menu_group_items_default_profile_id",
                table: "menu_group_items",
                column: "default_profile_id");

            migrationBuilder.AddForeignKey(
                name: "fk_menu_group_items_profiles_default_profile_id",
                table: "menu_group_items",
                column: "default_profile_id",
                principalTable: "profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_menu_group_items_profiles_default_profile_id",
                table: "menu_group_items");

            migrationBuilder.DropIndex(
                name: "ix_menu_group_items_default_profile_id",
                table: "menu_group_items");

            migrationBuilder.DropColumn(
                name: "default_profile_id",
                table: "menu_group_items");
        }
    }
}
