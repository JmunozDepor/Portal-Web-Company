using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexingHuecosReales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_logs_company_id",
                table: "audit_logs");

            migrationBuilder.CreateIndex(
                name: "ix_users_organization_id_is_active",
                table: "users",
                columns: new[] { "organization_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_menus_is_active",
                table: "menus",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_company_id_created_at",
                table: "audit_logs",
                columns: new[] { "company_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_organization_id_is_active",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_menus_is_active",
                table: "menus");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_company_id_created_at",
                table: "audit_logs");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_company_id",
                table: "audit_logs",
                column: "company_id");
        }
    }
}
