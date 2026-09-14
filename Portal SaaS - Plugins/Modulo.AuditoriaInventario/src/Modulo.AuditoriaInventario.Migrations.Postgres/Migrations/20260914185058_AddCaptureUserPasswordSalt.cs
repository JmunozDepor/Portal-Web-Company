using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.AuditoriaInventario.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureUserPasswordSalt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "password_salt",
                table: "capture_users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password_salt",
                table: "capture_users");
        }
    }
}
