using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulo.FacturasProveedor.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fp_approver_groups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    is_finance = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fp_approver_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fp_config_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    entity_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    before_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    after_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fp_config_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fp_invoice_reviews",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    received_id = table.Column<long>(type: "bigint", nullable: false),
                    issuer_tax_id = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    issuer_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    dte_type = table.Column<int>(type: "int", nullable: false),
                    folio = table.Column<long>(type: "bigint", nullable: false),
                    total_amount = table.Column<long>(type: "bigint", nullable: false),
                    sii_received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    legal_deadline_on = table.Column<DateOnly>(type: "date", nullable: false),
                    warn_deadline_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    proposed_action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    recommended_action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    decided_action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    decision_source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    decided_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    claim_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    assigned_group_id = table.Column<long>(type: "bigint", nullable: true),
                    is_simulated = table.Column<bool>(type: "bit", nullable: false),
                    decision_number = table.Column<int>(type: "int", nullable: false),
                    evaluation_count = table.Column<int>(type: "int", nullable: false),
                    last_evaluated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    last_error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fp_invoice_reviews", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fp_rule_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rule_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    action_days = table.Column<int>(type: "int", nullable: true),
                    parameters_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fp_rule_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fp_sap_queries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    query_kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    sql_text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fp_sap_queries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fp_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_deadline_days = table.Column<int>(type: "int", nullable: false),
                    escalate_days_before = table.Column<int>(type: "int", nullable: false),
                    cutoff_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    final_action_when_no_objection = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    process_from_date = table.Column<DateOnly>(type: "date", nullable: true),
                    mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    daily_email_digest = table.Column<bool>(type: "bit", nullable: false),
                    finance_group_id = table.Column<long>(type: "bigint", nullable: true),
                    inbox_cursor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    inbox_cursor_id = table.Column<long>(type: "bigint", nullable: false),
                    last_digest_on = table.Column<DateOnly>(type: "date", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fp_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fp_supplier_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    card_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    tax_id = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    supplier_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    condition = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    approver_group_id = table.Column<long>(type: "bigint", nullable: true),
                    backup_group_id = table.Column<long>(type: "bigint", nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fp_supplier_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fp_approver_group_members",
                columns: table => new
                {
                    group_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fp_approver_group_members", x => new { x.group_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_fp_approver_group_members_fp_approver_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "fp_approver_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fp_review_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    review_id = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fp_review_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_fp_review_events_fp_invoice_reviews_review_id",
                        column: x => x.review_id,
                        principalTable: "fp_invoice_reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fp_review_rule_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    review_id = table.Column<long>(type: "bigint", nullable: false),
                    rule_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    data_json = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fp_review_rule_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_fp_review_rule_results_fp_invoice_reviews_review_id",
                        column: x => x.review_id,
                        principalTable: "fp_invoice_reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fp_supplier_rule_overrides",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    rule_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    action_days = table.Column<int>(type: "int", nullable: true),
                    parameters_json = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fp_supplier_rule_overrides", x => x.id);
                    table.ForeignKey(
                        name: "FK_fp_supplier_rule_overrides_fp_supplier_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "fp_supplier_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fp_approver_groups_company_id",
                table: "fp_approver_groups",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_fp_config_events_company_id_occurred_at",
                table: "fp_config_events",
                columns: new[] { "company_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_fp_invoice_reviews_company_id_status",
                table: "fp_invoice_reviews",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_fp_invoice_reviews_company_id_received_id",
                table: "fp_invoice_reviews",
                columns: new[] { "company_id", "received_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fp_review_events_review_id",
                table: "fp_review_events",
                column: "review_id");

            migrationBuilder.CreateIndex(
                name: "IX_fp_review_rule_results_review_id",
                table: "fp_review_rule_results",
                column: "review_id");

            migrationBuilder.CreateIndex(
                name: "uq_fp_rule_settings_company_id_rule_code",
                table: "fp_rule_settings",
                columns: new[] { "company_id", "rule_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_fp_sap_queries_company_id_query_kind",
                table: "fp_sap_queries",
                columns: new[] { "company_id", "query_kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_fp_settings_company_id",
                table: "fp_settings",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_fp_supplier_profiles_company_id_tax_id",
                table: "fp_supplier_profiles",
                columns: new[] { "company_id", "tax_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_fp_supplier_rule_overrides_profile_id_rule_code",
                table: "fp_supplier_rule_overrides",
                columns: new[] { "profile_id", "rule_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fp_approver_group_members");

            migrationBuilder.DropTable(
                name: "fp_config_events");

            migrationBuilder.DropTable(
                name: "fp_review_events");

            migrationBuilder.DropTable(
                name: "fp_review_rule_results");

            migrationBuilder.DropTable(
                name: "fp_rule_settings");

            migrationBuilder.DropTable(
                name: "fp_sap_queries");

            migrationBuilder.DropTable(
                name: "fp_settings");

            migrationBuilder.DropTable(
                name: "fp_supplier_rule_overrides");

            migrationBuilder.DropTable(
                name: "fp_approver_groups");

            migrationBuilder.DropTable(
                name: "fp_invoice_reviews");

            migrationBuilder.DropTable(
                name: "fp_supplier_profiles");
        }
    }
}
