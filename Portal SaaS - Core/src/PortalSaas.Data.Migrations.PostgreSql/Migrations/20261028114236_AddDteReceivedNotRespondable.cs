using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddDteReceivedNotRespondable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "not_respondable_reason",
                table: "dte_received",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "not_respondable_reason",
                table: "dte_received");
        }
    }
}
