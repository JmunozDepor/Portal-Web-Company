using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortalSaas.Data.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreMenuAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "actions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    @event = table.Column<string>(name: "event", type: "nvarchar(100)", maxLength: 100, nullable: false),
                    detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ip_address = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_logs_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_audit_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "menu_groups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "menus",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    parent_menu_id = table.Column<long>(type: "bigint", nullable: true),
                    origin_module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    icon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    page_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    order = table.Column<int>(type: "int", nullable: false),
                    level = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menus", x => x.id);
                    table.ForeignKey(
                        name: "fk_menus_menus_parent_menu_id",
                        column: x => x.parent_menu_id,
                        principalTable: "menus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_menu_groups",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    menu_group_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_menu_groups", x => new { x.user_id, x.menu_group_id, x.company_id });
                    table.ForeignKey(
                        name: "fk_user_menu_groups_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_menu_groups_menu_groups_menu_group_id",
                        column: x => x.menu_group_id,
                        principalTable: "menu_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_menu_groups_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_group_items",
                columns: table => new
                {
                    menu_group_id = table.Column<long>(type: "bigint", nullable: false),
                    menu_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_group_items", x => new { x.menu_group_id, x.menu_id });
                    table.ForeignKey(
                        name: "fk_menu_group_items_menu_groups_menu_group_id",
                        column: x => x.menu_group_id,
                        principalTable: "menu_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_menu_group_items_menus_menu_id",
                        column: x => x.menu_id,
                        principalTable: "menus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profile_actions",
                columns: table => new
                {
                    profile_id = table.Column<long>(type: "bigint", nullable: false),
                    action_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile_actions", x => new { x.profile_id, x.action_id });
                    table.ForeignKey(
                        name: "fk_profile_actions_actions_action_id",
                        column: x => x.action_id,
                        principalTable: "actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_profile_actions_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_menu_profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    menu_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    profile_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_menu_profiles", x => new { x.user_id, x.menu_id, x.company_id });
                    table.ForeignKey(
                        name: "fk_user_menu_profiles_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_menu_profiles_menus_menu_id",
                        column: x => x.menu_id,
                        principalTable: "menus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_menu_profiles_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_menu_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_actions_code",
                table: "actions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_company_id",
                table: "audit_logs",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_at",
                table: "audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_group_items_menu_id",
                table: "menu_group_items",
                column: "menu_id");

            migrationBuilder.CreateIndex(
                name: "ix_menus_origin_module_code",
                table: "menus",
                columns: new[] { "origin_module", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_menus_page_path",
                table: "menus",
                column: "page_path");

            migrationBuilder.CreateIndex(
                name: "ix_menus_parent_menu_id",
                table: "menus",
                column: "parent_menu_id");

            migrationBuilder.CreateIndex(
                name: "ix_profile_actions_action_id",
                table: "profile_actions",
                column: "action_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_menu_groups_company_id",
                table: "user_menu_groups",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_menu_groups_menu_group_id",
                table: "user_menu_groups",
                column: "menu_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_menu_profiles_company_id",
                table: "user_menu_profiles",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_menu_profiles_menu_id",
                table: "user_menu_profiles",
                column: "menu_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_menu_profiles_profile_id",
                table: "user_menu_profiles",
                column: "profile_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "menu_group_items");

            migrationBuilder.DropTable(
                name: "profile_actions");

            migrationBuilder.DropTable(
                name: "user_menu_groups");

            migrationBuilder.DropTable(
                name: "user_menu_profiles");

            migrationBuilder.DropTable(
                name: "actions");

            migrationBuilder.DropTable(
                name: "menu_groups");

            migrationBuilder.DropTable(
                name: "menus");

            migrationBuilder.DropTable(
                name: "profiles");
        }
    }
}
