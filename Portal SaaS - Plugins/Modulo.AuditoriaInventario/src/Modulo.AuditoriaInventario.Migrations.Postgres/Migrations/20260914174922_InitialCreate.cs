using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modulo.AuditoriaInventario.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "capture_users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capture_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    line = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    loaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "frozen_inventory_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    inventory_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    loaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_frozen_inventory_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_frozen_inventory_snapshots_branches",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_sectors",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_sectors", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_sectors_branches",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "capture_auth_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capture_user_id = table.Column<long>(type: "bigint", nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capture_auth_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_capture_auth_tokens_capture_users",
                        column: x => x.capture_user_id,
                        principalTable: "capture_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<long>(type: "bigint", nullable: false),
                    inventory_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    responsible_user_id = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    validate_against_master = table.Column<bool>(type: "boolean", nullable: false),
                    master_snapshot_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_sessions_branches",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_sessions_capture_users",
                        column: x => x.responsible_user_id,
                        principalTable: "capture_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "frozen_inventory_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_frozen_inventory_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_frozen_inventory_lines_frozen_inventory_snapshots",
                        column: x => x.snapshot_id,
                        principalTable: "frozen_inventory_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_captures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sector_id = table.Column<long>(type: "bigint", nullable: false),
                    barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    in_master = table.Column<bool>(type: "boolean", nullable: true),
                    captured_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_captures", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_captures_capture_users",
                        column: x => x.captured_by_user_id,
                        principalTable: "capture_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_captures_inventory_sectors",
                        column: x => x.sector_id,
                        principalTable: "inventory_sectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_captures_inventory_sessions",
                        column: x => x.session_id,
                        principalTable: "inventory_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_differences",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    sector_id = table.Column<long>(type: "bigint", nullable: true),
                    barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    captured_quantity = table.Column<int>(type: "integer", nullable: false),
                    frozen_quantity = table.Column<int>(type: "integer", nullable: false),
                    quantity_diff = table.Column<int>(type: "integer", nullable: false),
                    amount_diff = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_differences", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_differences_frozen_inventory_snapshots",
                        column: x => x.snapshot_id,
                        principalTable: "frozen_inventory_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_differences_inventory_sessions",
                        column: x => x.session_id,
                        principalTable: "inventory_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_adjustments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    difference_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    proposed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_inventory_differences",
                        column: x => x.difference_id,
                        principalTable: "inventory_differences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sap_adjustment_queue_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    adjustment_id = table.Column<long>(type: "bigint", nullable: false),
                    sap_company_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sap_warehouse_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sap_material_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sap_document_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sap_adjustment_queue_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_sap_adjustment_queue_items_inventory_adjustments",
                        column: x => x.adjustment_id,
                        principalTable: "inventory_adjustments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_branches_company_id_branch_code",
                table: "branches",
                columns: new[] { "company_id", "branch_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_capture_auth_tokens_capture_user_id",
                table: "capture_auth_tokens",
                column: "capture_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_capture_auth_tokens_token",
                table: "capture_auth_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_capture_users_company_id_username",
                table: "capture_users",
                columns: new[] { "company_id", "username" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_frozen_inventory_lines_snapshot_barcode",
                table: "frozen_inventory_lines",
                columns: new[] { "snapshot_id", "barcode" });

            migrationBuilder.CreateIndex(
                name: "IX_frozen_inventory_snapshots_branch_id",
                table: "frozen_inventory_snapshots",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_frozen_snapshots_company_branch_inventory_number",
                table: "frozen_inventory_snapshots",
                columns: new[] { "company_id", "branch_id", "inventory_number" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_difference_id",
                table: "inventory_adjustments",
                column: "difference_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_status",
                table: "inventory_adjustments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_captures_captured_by_user_id",
                table: "inventory_captures",
                column: "captured_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_captures_sector_id",
                table: "inventory_captures",
                column: "sector_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_captures_session_sector_barcode",
                table: "inventory_captures",
                columns: new[] { "session_id", "sector_id", "barcode" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_differences_session_id",
                table: "inventory_differences",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_differences_snapshot_id",
                table: "inventory_differences",
                column: "snapshot_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_sectors_branch_id",
                table: "inventory_sectors",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_sectors_company_id",
                table: "inventory_sectors",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_sessions_branch_id",
                table: "inventory_sessions",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_sessions_company_branch_status",
                table: "inventory_sessions",
                columns: new[] { "company_id", "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_sessions_responsible_user_id",
                table: "inventory_sessions",
                column: "responsible_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_company_id_product_code",
                table: "products",
                columns: new[] { "company_id", "product_code" });

            migrationBuilder.CreateIndex(
                name: "uq_products_company_id_barcode",
                table: "products",
                columns: new[] { "company_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sap_adjustment_queue_items_adjustment_id",
                table: "sap_adjustment_queue_items",
                column: "adjustment_id");

            migrationBuilder.CreateIndex(
                name: "ix_sap_adjustment_queue_items_status",
                table: "sap_adjustment_queue_items",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capture_auth_tokens");

            migrationBuilder.DropTable(
                name: "frozen_inventory_lines");

            migrationBuilder.DropTable(
                name: "inventory_captures");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "sap_adjustment_queue_items");

            migrationBuilder.DropTable(
                name: "inventory_sectors");

            migrationBuilder.DropTable(
                name: "inventory_adjustments");

            migrationBuilder.DropTable(
                name: "inventory_differences");

            migrationBuilder.DropTable(
                name: "frozen_inventory_snapshots");

            migrationBuilder.DropTable(
                name: "inventory_sessions");

            migrationBuilder.DropTable(
                name: "branches");

            migrationBuilder.DropTable(
                name: "capture_users");
        }
    }
}
