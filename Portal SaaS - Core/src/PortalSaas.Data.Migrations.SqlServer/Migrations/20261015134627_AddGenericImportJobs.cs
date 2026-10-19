using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddGenericImportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "analyze_stock",
                table: "generic_import_configs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "generic_import_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    module = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    document_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    line_type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    business_partner_card_code = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    config_id = table.Column<int>(type: "int", nullable: true),
                    sales_person_code = table.Column<int>(type: "int", nullable: true),
                    file_name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    file_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    file_content = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    progress_current = table.Column<int>(type: "int", nullable: false),
                    progress_total = table.Column<int>(type: "int", nullable: false),
                    progress_message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    result_message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    result_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    documents_created = table.Column<int>(type: "int", nullable: false),
                    documents_failed = table.Column<int>(type: "int", nullable: false),
                    documents_skipped = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    queued_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generic_import_jobs", x => x.id);
                    table.CheckConstraint("ck_generic_import_jobs_status", "status in ('Draft', 'Queued', 'Running', 'Completed', 'Failed')");
                    table.ForeignKey(
                        name: "fk_generic_import_jobs_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_generic_import_jobs_company_id_created_at",
                table: "generic_import_jobs",
                columns: new[] { "company_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_generic_import_jobs_status_created_at",
                table: "generic_import_jobs",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generic_import_jobs");

            migrationBuilder.DropColumn(
                name: "analyze_stock",
                table: "generic_import_configs");
        }
    }
}
