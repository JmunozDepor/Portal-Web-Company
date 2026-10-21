using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class GenericImportJobSaved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_generic_import_jobs_status",
                table: "generic_import_jobs");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "saved_at",
                table: "generic_import_jobs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "saved_reason",
                table: "generic_import_jobs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "submitted_by_user_id",
                table: "generic_import_jobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "submitted_by_username",
                table: "generic_import_jobs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_generic_import_jobs_status",
                table: "generic_import_jobs",
                sql: "status in ('Draft', 'Saved', 'Queued', 'Running', 'Completed', 'Failed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_generic_import_jobs_status",
                table: "generic_import_jobs");

            migrationBuilder.DropColumn(
                name: "saved_at",
                table: "generic_import_jobs");

            migrationBuilder.DropColumn(
                name: "saved_reason",
                table: "generic_import_jobs");

            migrationBuilder.DropColumn(
                name: "submitted_by_user_id",
                table: "generic_import_jobs");

            migrationBuilder.DropColumn(
                name: "submitted_by_username",
                table: "generic_import_jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_generic_import_jobs_status",
                table: "generic_import_jobs",
                sql: "status in ('Draft', 'Queued', 'Running', 'Completed', 'Failed')");
        }
    }
}
