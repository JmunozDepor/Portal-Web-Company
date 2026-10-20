using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_document_settings",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    draft_retention_days = table.Column<int>(type: "int", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_document_settings", x => x.company_id);
                    table.ForeignKey(
                        name: "fk_company_document_settings_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    engine = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    document_type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    partner_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: true),
                    line_count = table.Column<int>(type: "int", nullable: false),
                    total = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_by_username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    updated_by_username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    sap_doc_entry = table.Column<int>(type: "int", nullable: true),
                    error_message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_drafts", x => x.id);
                    table.CheckConstraint("ck_document_drafts_status", "status in ('draft', 'sending', 'sent', 'error')");
                    table.ForeignKey(
                        name: "fk_document_drafts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_drafts_company_id_created_by_user_id",
                table: "document_drafts",
                columns: new[] { "company_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_drafts_company_id_engine_document_type_status_updated_at",
                table: "document_drafts",
                columns: new[] { "company_id", "engine", "document_type", "status", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_document_settings");

            migrationBuilder.DropTable(
                name: "document_drafts");
        }
    }
}
