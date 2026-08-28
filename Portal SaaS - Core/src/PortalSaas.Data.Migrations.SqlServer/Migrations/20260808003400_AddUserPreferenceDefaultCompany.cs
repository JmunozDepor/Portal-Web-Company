using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferenceDefaultCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_company_id",
                table: "user_preferences",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_preferences_default_company_id",
                table: "user_preferences",
                column: "default_company_id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_preferences_companies_default_company_id",
                table: "user_preferences",
                column: "default_company_id",
                principalTable: "companies",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_preferences_companies_default_company_id",
                table: "user_preferences");

            migrationBuilder.DropIndex(
                name: "ix_user_preferences_default_company_id",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "default_company_id",
                table: "user_preferences");
        }
    }
}
