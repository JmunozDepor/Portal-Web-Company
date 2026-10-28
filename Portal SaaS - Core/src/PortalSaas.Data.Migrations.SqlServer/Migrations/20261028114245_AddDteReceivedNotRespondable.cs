using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
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
                type: "nvarchar(200)",
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
