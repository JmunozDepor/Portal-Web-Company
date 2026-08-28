using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PortalSaas.Data.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    country = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizations", x => x.id);
                    table.CheckConstraint("ck_organizations_mode", "mode in ('saas', 'on_premise')");
                    table.CheckConstraint("ck_organizations_status", "status in ('trial', 'active', 'suspended', 'cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    user_limit = table.Column<int>(type: "integer", nullable: true),
                    company_limit = table.Column<int>(type: "integer", nullable: true),
                    monthly_transaction_limit = table.Column<int>(type: "integer", nullable: true),
                    monthly_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "CLP"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_modules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_core = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_modules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "instances",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    engine_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    technical_username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    technical_secret_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instances", x => x.id);
                    table.CheckConstraint("ck_instances_engine_type", "engine_type in ('hana', 'sqlserver')");
                    table.ForeignKey(
                        name: "fk_instances_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "on_premise_licenses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activation_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    installation_fingerprint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_on_premise_licenses", x => x.id);
                    table.CheckConstraint("ck_on_premise_licenses_status", "status in ('active', 'revoked', 'expired')");
                    table.ForeignKey(
                        name: "fk_on_premise_licenses_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    password_salt = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_admin = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    failed_login_attempts = table.Column<int>(type: "integer", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    has_two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    two_factor_secret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    external_payment_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.CheckConstraint("ck_subscriptions_status", "status in ('trial', 'active', 'past_due', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_subscriptions_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subscriptions_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organization_modules",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<long>(type: "bigint", nullable: false),
                    contracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_modules", x => new { x.organization_id, x.module_id });
                    table.ForeignKey(
                        name: "fk_organization_modules_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_organization_modules_platform_modules_module_id",
                        column: x => x.module_id,
                        principalTable: "platform_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_modules",
                columns: table => new
                {
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    module_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_modules", x => new { x.plan_id, x.module_id });
                    table.ForeignKey(
                        name: "fk_plan_modules_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_plan_modules_platform_modules_module_id",
                        column: x => x.module_id,
                        principalTable: "platform_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    database_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    service_layer_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    integration_username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    integration_secret_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    country = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                    table.ForeignKey(
                        name: "fk_companies_instances_instance_id",
                        column: x => x.instance_id,
                        principalTable: "instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_companies_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usage_metrics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metric_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    period = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_metrics", x => x.id);
                    table.ForeignKey(
                        name: "fk_usage_metrics_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_usage_metrics_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_companies_code",
                table: "companies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_companies_instance_id",
                table: "companies",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_organization_id",
                table: "companies",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_instances_organization_id_name",
                table: "instances",
                columns: new[] { "organization_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_on_premise_licenses_activation_key",
                table: "on_premise_licenses",
                column: "activation_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_on_premise_licenses_organization_id",
                table: "on_premise_licenses",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_modules_module_id",
                table: "organization_modules",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_modules_module_id",
                table: "plan_modules",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ix_plans_code",
                table: "plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_modules_code",
                table: "platform_modules",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_organization_id",
                table: "subscriptions",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_plan_id",
                table: "subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_usage_metrics_company_id",
                table: "usage_metrics",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_usage_metrics_period",
                table: "usage_metrics",
                columns: new[] { "organization_id", "period", "metric_name" });

            migrationBuilder.CreateIndex(
                name: "ix_users_organization_id_username",
                table: "users",
                columns: new[] { "organization_id", "username" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "on_premise_licenses");

            migrationBuilder.DropTable(
                name: "organization_modules");

            migrationBuilder.DropTable(
                name: "plan_modules");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "usage_metrics");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "platform_modules");

            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "instances");

            migrationBuilder.DropTable(
                name: "organizations");
        }
    }
}
