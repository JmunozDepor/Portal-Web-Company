using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Modulo.Rendiciones.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalServiceProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_external_service_usages_company_service_year_month",
                table: "external_service_usages");

            migrationBuilder.DropColumn(
                name: "company_id",
                table: "external_service_usages");

            migrationBuilder.DropColumn(
                name: "service_name",
                table: "external_service_usages");

            migrationBuilder.AddColumn<long>(
                name: "provider_id",
                table: "external_service_usages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "external_service_providers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    api_key_encrypted = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    monthly_limit = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_service_providers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_external_service_usages_provider_year_month",
                table: "external_service_usages",
                columns: new[] { "provider_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_service_providers_company_service_priority",
                table: "external_service_providers",
                columns: new[] { "company_id", "service_type", "priority" });

            migrationBuilder.AddForeignKey(
                name: "fk_external_service_usages_external_service_providers",
                table: "external_service_usages",
                column: "provider_id",
                principalTable: "external_service_providers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_external_service_usages_external_service_providers",
                table: "external_service_usages");

            migrationBuilder.DropTable(
                name: "external_service_providers");

            migrationBuilder.DropIndex(
                name: "uq_external_service_usages_provider_year_month",
                table: "external_service_usages");

            migrationBuilder.DropColumn(
                name: "provider_id",
                table: "external_service_usages");

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                table: "external_service_usages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "service_name",
                table: "external_service_usages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "uq_external_service_usages_company_service_year_month",
                table: "external_service_usages",
                columns: new[] { "company_id", "service_name", "year", "month" },
                unique: true);
        }
    }
}
