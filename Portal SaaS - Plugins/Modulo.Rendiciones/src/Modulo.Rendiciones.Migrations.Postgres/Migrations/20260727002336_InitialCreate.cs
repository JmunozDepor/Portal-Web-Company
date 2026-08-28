using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modulo.Rendiciones.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    applies_tax = table.Column<bool>(type: "boolean", nullable: false),
                    tax_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_approval_groups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_approval_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_funds",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cost_center_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settlement_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_funds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_receipts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sap_gl_account = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_mileage = table.Column<bool>(type: "boolean", nullable: false),
                    rate_per_km = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_service_usages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    used_units = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_service_usages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_cost_centers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cost_center_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_cost_centers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_approval_group_levels",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    expense_approval_group_id = table.Column<long>(type: "bigint", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_approval_group_levels", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_approval_group_levels_expense_approval_groups",
                        column: x => x.expense_approval_group_id,
                        principalTable: "expense_approval_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expense_approval_group_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    expense_approval_group_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_approval_group_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_approval_group_members_expense_approval_groups",
                        column: x => x.expense_approval_group_id,
                        principalTable: "expense_approval_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expense_reports",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_fund_id = table.Column<long>(type: "bigint", nullable: true),
                    cost_center_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cost_center_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    round = table.Column<int>(type: "integer", nullable: false),
                    expense_approval_group_id = table.Column<long>(type: "bigint", nullable: true),
                    current_level = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_reports_expense_approval_groups",
                        column: x => x.expense_approval_group_id,
                        principalTable: "expense_approval_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_reports_expense_funds",
                        column: x => x.expense_fund_id,
                        principalTable: "expense_funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_policies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_type_id = table.Column<long>(type: "bigint", nullable: false),
                    max_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    is_blocking = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_policies_expense_types",
                        column: x => x.expense_type_id,
                        principalTable: "expense_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_report_actions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    expense_report_id = table.Column<long>(type: "bigint", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_report_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_report_actions_expense_reports",
                        column: x => x.expense_report_id,
                        principalTable: "expense_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_report_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expense_report_id = table.Column<long>(type: "bigint", nullable: true),
                    expense_type_id = table.Column<long>(type: "bigint", nullable: true),
                    document_type_id = table.Column<long>(type: "bigint", nullable: true),
                    expense_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    document_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    supplier_tax_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expense_receipt_id = table.Column<long>(type: "bigint", nullable: true),
                    origin = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    destination = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    distance_km = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    applied_rate_per_km = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_report_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_report_lines_document_types",
                        column: x => x.document_type_id,
                        principalTable: "document_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_report_lines_expense_receipts",
                        column: x => x.expense_receipt_id,
                        principalTable: "expense_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_report_lines_expense_reports",
                        column: x => x.expense_report_id,
                        principalTable: "expense_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_report_lines_expense_types",
                        column: x => x.expense_type_id,
                        principalTable: "expense_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_types_company_id",
                table: "document_types",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_expense_approval_group_levels_group_id_level",
                table: "expense_approval_group_levels",
                columns: new[] { "expense_approval_group_id", "level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_expense_approval_group_members_group_id_user_id",
                table: "expense_approval_group_members",
                columns: new[] { "expense_approval_group_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_funds_company_id",
                table: "expense_funds",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_policies_expense_type_id",
                table: "expense_policies",
                column: "expense_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_report_actions_expense_report_id",
                table: "expense_report_actions",
                column: "expense_report_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_report_lines_company_id",
                table: "expense_report_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_report_lines_document_type_id",
                table: "expense_report_lines",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_report_lines_expense_receipt_id",
                table: "expense_report_lines",
                column: "expense_receipt_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_report_lines_expense_report_id",
                table: "expense_report_lines",
                column: "expense_report_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_report_lines_expense_type_id",
                table: "expense_report_lines",
                column: "expense_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_reports_company_id",
                table: "expense_reports",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_reports_expense_approval_group_id",
                table: "expense_reports",
                column: "expense_approval_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_reports_expense_fund_id",
                table: "expense_reports",
                column: "expense_fund_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_types_company_id",
                table: "expense_types",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "uq_external_service_usages_company_service_year_month",
                table: "external_service_usages",
                columns: new[] { "company_id", "service_name", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_cost_centers_company_id_user_id",
                table: "user_cost_centers",
                columns: new[] { "company_id", "user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_approval_group_levels");

            migrationBuilder.DropTable(
                name: "expense_approval_group_members");

            migrationBuilder.DropTable(
                name: "expense_policies");

            migrationBuilder.DropTable(
                name: "expense_report_actions");

            migrationBuilder.DropTable(
                name: "expense_report_lines");

            migrationBuilder.DropTable(
                name: "external_service_usages");

            migrationBuilder.DropTable(
                name: "user_cost_centers");

            migrationBuilder.DropTable(
                name: "document_types");

            migrationBuilder.DropTable(
                name: "expense_receipts");

            migrationBuilder.DropTable(
                name: "expense_reports");

            migrationBuilder.DropTable(
                name: "expense_types");

            migrationBuilder.DropTable(
                name: "expense_approval_groups");

            migrationBuilder.DropTable(
                name: "expense_funds");
        }
    }
}
